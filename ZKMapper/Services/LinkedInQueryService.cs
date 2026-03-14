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

    public async Task NavigateToSearchResultsAsync(
        IPage page,
        string searchUrl,
        string keyword,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("QueryExecution");
        AppLog.Next("navigating to LinkedIn search results", "QueryExecution", "navigate-search-results", $"keyword={keyword}");
        AppLog.Action("navigating browser", "QueryExecution", "navigate-search-results", $"url={searchUrl}");

        await _retryService.ExecuteAsync(async token =>
        {
            await page.GotoAsync(searchUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            var resultsContainer = await page.FirstVisibleAsync(LinkedInSelectors.ResultsContainerCandidates, token);
            await resultsContainer.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });

            await _humanDelayService.DelayAsync(DelayProfile.Navigation, "allow LinkedIn search results to finish rendering", token);
        }, cancellationToken);

        AppLog.Result("search results page loaded", "QueryExecution", "navigate-search-results", $"keyword={keyword}");
        AppLog.Data($"url={page.Url}", "QueryExecution", "navigate-search-results", $"keyword={keyword};url={page.Url}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "QueryExecution", "navigate-search-results", cancellationToken);
    }

    public async Task<IReadOnlyList<ContactDiscoveryTarget>> DiscoverContactsAsync(
        IPage page,
        string query,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ProfileDiscovery");
        AppLog.Step("starting profile discovery", "ProfileDiscovery", "discover-profiles", $"query={query};url={page.Url}");
        var discovered = new Dictionary<string, ContactDiscoveryTarget>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var countBeforePass = discovered.Count;
            AppLog.Data(
                $"discovery pass start;currentCount={countBeforePass}",
                "ProfileDiscovery",
                "discover-profiles",
                $"query={query};currentCount={countBeforePass}");
            await CaptureVisibleTargetsAsync(page, discovered, query, cancellationToken);

            if (await TryClickShowMoreAsync(page, cancellationToken))
            {
                AppLog.Result("show more results triggered", "ProfileDiscovery", "show-more", $"query={query}");
                await _humanDelayService.DelayAsync(DelayProfile.Scroll, "allow additional LinkedIn search results after show more", cancellationToken);
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
        AppLog.Step("checking for additional results control", "ProfileDiscovery", "show-more", $"queryUrl={page.Url}");
        var button = await page.FirstVisibleOrNullAsync(LinkedInSelectors.ShowMoreButtonCandidates, cancellationToken);
        if (button is null)
        {
            AppLog.Result(
                "show more button not present",
                "ProfileDiscovery",
                "show-more",
                $"selectorCandidates={string.Join(" | ", LinkedInSelectors.ShowMoreButtonCandidates)};queryUrl={page.Url}");
            return false;
        }

        return await _retryService.ExecuteAsync(async token =>
        {
            AppLog.Action("click", "ProfileDiscovery", "click-show-more", $"selectorCandidates={string.Join(" | ", LinkedInSelectors.ShowMoreButtonCandidates)}");
            await button.ClickAsync();
            await _humanDelayService.DelayAsync(DelayProfile.Scroll, "wait after clicking show more results", token);
            return true;
        }, cancellationToken);
    }
}
