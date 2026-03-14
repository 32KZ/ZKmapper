using Microsoft.Playwright;
using ZKMapper.Infrastructure;
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
        using var timer = ExecutionTimer.Start("QueryExecution");
        AppLog.Step("executing search", "QueryExecution", "submit-query", $"keyword={query}");
        var searchInput = await page.FirstVisibleAsync(LinkedInSelectors.PeopleSearchInputCandidates, cancellationToken);

        await _retryService.ExecuteAsync(async token =>
        {
            AppLog.Action("click", "QueryExecution", "focus-search-input", $"selectorCandidates={string.Join(" | ", LinkedInSelectors.PeopleSearchInputCandidates)}");
            await searchInput.ClickAsync();
            await searchInput.FillAsync(string.Empty);
            await searchInput.PressAsync("Control+A");
            await searchInput.PressAsync("Backspace");
            AppLog.Action("type", "QueryExecution", "enter-query-text", $"query={query}");
            await searchInput.PressSequentiallyAsync(query, new LocatorPressSequentiallyOptions { Delay = 85 });
            AppLog.Action("press-enter", "QueryExecution", "submit-query", $"query={query}");
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
            await _humanDelayService.DelayAsync(2, 4, "allow LinkedIn people results to render after query", token);
        }, cancellationToken);

        AppLog.Data($"queryUrl={page.Url}", "QueryExecution", "submit-query", $"query={query};queryUrl={page.Url}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "QueryExecution", "submit-query", cancellationToken);
        AppLog.Result("search query executed", "QueryExecution", "submit-query", $"keyword={query}");
    }

    public async Task<IReadOnlyList<ContactDiscoveryTarget>> DiscoverContactsAsync(
        IPage page,
        string query,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ProfileDiscovery");
        var discovered = new Dictionary<string, ContactDiscoveryTarget>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var countBeforePass = discovered.Count;
            await CaptureVisibleTargetsAsync(page, discovered, query, cancellationToken);

            if (await TryClickShowMoreAsync(page, cancellationToken))
            {
                AppLog.Result("show more results triggered", "ProfileDiscovery", "show-more", $"query={query}");
                await _humanDelayService.DelayAsync(2, 4, "allow additional LinkedIn search results after show more", cancellationToken);
                continue;
            }

            await _scrollExhaustionService.ScrollToEndAsync(page, cancellationToken);
            await CaptureVisibleTargetsAsync(page, discovered, query, cancellationToken);

            if (discovered.Count == countBeforePass)
            {
                AppLog.Result("profile discovery complete", "ProfileDiscovery", "discover-profiles", $"query={query};count={discovered.Count}");
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
            AppLog.Data(
                $"found profile name={text};url={absoluteHref};position={index + 1}",
                "ProfileDiscovery",
                "capture-visible-targets",
                $"query={query};profileName={text};profileUrl={absoluteHref};position={index + 1}");
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
            AppLog.Action("click", "ProfileDiscovery", "click-show-more", $"selectorCandidates={string.Join(" | ", LinkedInSelectors.ShowMoreButtonCandidates)}");
            await button.ClickAsync();
            await _humanDelayService.DelayAsync(2, 4, "wait after clicking show more results", token);
            return true;
        }, cancellationToken);
    }
}
