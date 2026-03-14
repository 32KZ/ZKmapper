using Microsoft.Playwright;
using Serilog;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class LinkedInQueryService
{
    private readonly RetryService _retryService;
    private readonly ScrollExhaustionService _scrollExhaustionService;
    private readonly HumanDelayService _humanDelayService;

    public LinkedInQueryService(
        RetryService retryService,
        ScrollExhaustionService scrollExhaustionService,
        HumanDelayService humanDelayService)
    {
        _retryService = retryService;
        _scrollExhaustionService = scrollExhaustionService;
        _humanDelayService = humanDelayService;
    }

    public string BuildQuery(string country, string title)
    {
        return $"{country.Trim()} {title.Trim()}".Trim();
    }

    public async Task SubmitQueryAsync(IPage page, string query, CancellationToken cancellationToken)
    {
        var searchInput = await page.FirstVisibleAsync(LinkedInSelectors.PeopleSearchInputCandidates, cancellationToken);

        await _retryService.ExecuteAsync(async token =>
        {
            await searchInput.ClickAsync();
            await searchInput.FillAsync(string.Empty);
            await searchInput.PressAsync("Control+A");
            await searchInput.PressAsync("Backspace");
            await searchInput.PressSequentiallyAsync(query, new LocatorPressSequentiallyOptions { Delay = 85 });
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
            await _humanDelayService.DelayAsync(2, 4, token);
        }, cancellationToken);

        Log.Information("Search query executed: {Query}", query);
    }

    public async Task<IReadOnlyList<ContactDiscoveryTarget>> DiscoverContactsAsync(
        IPage page,
        string query,
        CancellationToken cancellationToken)
    {
        var discovered = new Dictionary<string, ContactDiscoveryTarget>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var countBeforePass = discovered.Count;
            await CaptureVisibleTargetsAsync(page, discovered, query, cancellationToken);

            if (await TryClickShowMoreAsync(page, cancellationToken))
            {
                Log.Information("Use of show more results for query {Query}", query);
                await _humanDelayService.DelayAsync(2, 4, cancellationToken);
                continue;
            }

            await _scrollExhaustionService.ScrollToEndAsync(page, cancellationToken);
            await CaptureVisibleTargetsAsync(page, discovered, query, cancellationToken);

            if (discovered.Count == countBeforePass)
            {
                break;
            }
        }

        return discovered.Values.ToArray();
    }

    private static async Task<int> CaptureVisibleTargetsAsync(
        IPage page,
        IDictionary<string, ContactDiscoveryTarget> discovered,
        string query,
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
            Log.Information("Contact discovered for query {Query}: {ProfileUrl}", query, absoluteHref);
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
            await _humanDelayService.DelayAsync(2, 4, token);
            return true;
        }, cancellationToken);
    }
}
