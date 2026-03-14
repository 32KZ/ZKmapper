using Microsoft.Playwright;
using Serilog;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class LinkedInQueryService
{
    private readonly RetryService _retryService;

    public LinkedInQueryService(RetryService retryService)
    {
        _retryService = retryService;
    }

    public async Task SubmitQueryAsync(IPage page, string query, CancellationToken cancellationToken)
    {
        Log.Information("Constructed query {Query}", query);
        var searchInput = await page.FirstVisibleAsync(LinkedInSelectors.PeopleSearchInputCandidates, cancellationToken);

        await _retryService.ExecuteAsync(async token =>
        {
            await searchInput.ClickAsync();
            await searchInput.FillAsync(string.Empty);
            await searchInput.PressAsync("Control+A");
            await searchInput.PressAsync("Backspace");
            await searchInput.TypeAsync(query, new LocatorTypeOptions { Delay = 85 });
            await searchInput.PressAsync("Enter");
            token.ThrowIfCancellationRequested();
        }, cancellationToken);

        var resultsContainer = await page.FirstVisibleAsync(LinkedInSelectors.ResultsContainerCandidates, cancellationToken);
        await _retryService.ExecuteAsync(async token =>
        {
            await resultsContainer.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });
            await Task.Delay(TimeSpan.FromSeconds(3), token);
        }, cancellationToken);

        Log.Information("Query submission success for {Query}", query);
    }

    public async Task<IReadOnlyList<ContactDiscoveryTarget>> DiscoverContactsAsync(
        IPage page,
        string query,
        CancellationToken cancellationToken)
    {
        var discovered = new Dictionary<string, ContactDiscoveryTarget>(StringComparer.OrdinalIgnoreCase);
        var idleCycles = 0;
        double? priorHeight = null;

        while (idleCycles < 3)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var newlyAdded = await CaptureVisibleTargetsAsync(page, discovered, cancellationToken);

            if (newlyAdded > 0)
            {
                Log.Information("Discovery of contacts found {Count} new targets for query {Query}", newlyAdded, query);
                idleCycles = 0;
                continue;
            }

            if (await TryClickShowMoreAsync(page, cancellationToken))
            {
                Log.Information("Use of show more results for query {Query}", query);
                idleCycles = 0;
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                continue;
            }

            var currentHeight = await page.EvaluateAsync<double>("() => document.body.scrollHeight");
            await page.Mouse.WheelAsync(0, 1600);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            if (priorHeight.HasValue && Math.Abs(priorHeight.Value - currentHeight) < 1)
            {
                idleCycles++;
            }
            else
            {
                idleCycles = 0;
            }

            priorHeight = currentHeight;
        }

        return discovered.Values.ToArray();
    }

    private static async Task<int> CaptureVisibleTargetsAsync(
        IPage page,
        IDictionary<string, ContactDiscoveryTarget> discovered,
        CancellationToken cancellationToken)
    {
        var links = page.Locator(string.Join(", ", LinkedInSelectors.ProfileLinkCandidates));
        var count = await links.CountAsync();
        var added = 0;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var link = links.Nth(index);
            var href = await link.GetAttributeAsync("href");
            var text = (await link.InnerTextAsync()).Trim();

            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            var absoluteHref = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : $"https://www.linkedin.com{href}";

            if (!absoluteHref.Contains("/in/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (discovered.ContainsKey(absoluteHref))
            {
                continue;
            }

            discovered[absoluteHref] = new ContactDiscoveryTarget(absoluteHref, text, absoluteHref);
            added++;
        }

        return added;
    }

    private async Task<bool> TryClickShowMoreAsync(IPage page, CancellationToken cancellationToken)
    {
        var button = await page.FirstVisibleOrNullAsync(LinkedInSelectors.ShowMoreButtonCandidates, cancellationToken);
        if (button is null)
        {
            return false;
        }

        return await _retryService.ExecuteAsync(async token =>
        {
            await button.ClickAsync();
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            return true;
        }, cancellationToken);
    }
}
