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

    public async Task NavigateToPeoplePageAsync(
        IPage page,
        string searchUrl,
        string keyword,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("QueryExecution");
        AppLog.Action("navigating to LinkedIn people page", "QueryExecution", "navigate-people-page", $"url={searchUrl}");
        AppLog.Data($"url={searchUrl}", "QueryExecution", "navigate-people-page", $"keyword={keyword};url={searchUrl}");

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

        AppLog.Result("filtered people page loaded", "QueryExecution", "navigate-people-page", $"keyword={keyword}");
        AppLog.Data($"url={page.Url}", "QueryExecution", "navigate-people-page", $"keyword={keyword};url={page.Url}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "QueryExecution", "navigate-people-page", cancellationToken);
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
        var container = await FindHeroCardContainerAsync(page, cancellationToken);
        if (container is null)
        {
            AppLog.Data(
                "heroCardCount=0",
                "ProfileDiscovery",
                "capture-visible-targets",
                $"query={query};reason=hero-card-container-not-found");
            return 0;
        }

        var cardSelector = await ResolveHeroCardSelectorAsync(container, cancellationToken);
        if (cardSelector is null)
        {
            AppLog.Data(
                "heroCardCount=0",
                "ProfileDiscovery",
                "capture-visible-targets",
                $"query={query};reason=hero-card-selector-not-found");
            return 0;
        }

        var cards = container.Locator(cardSelector);
        var count = await cards.CountAsync();
        AppLog.Data($"heroCardCount={count}", "ProfileDiscovery", "capture-visible-targets", $"query={query};heroCardCount={count};cardSelector={cardSelector}");
        var added = 0;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var card = cards.Nth(index);
            if (!await card.IsVisibleAsync())
            {
                continue;
            }

            var position = index + 1;
            var visibleName = await ExtractCardNameAsync(card, cancellationToken);
            if (string.IsNullOrWhiteSpace(visibleName) || string.Equals(visibleName, "LinkedIn Member", StringComparison.OrdinalIgnoreCase))
            {
                AppLog.Data(
                    $"skipped hero card due to anonymous name;position={position}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"query={query};position={position}");
                continue;
            }

            var href = await ExtractCardHrefAsync(card, cancellationToken);

            if (string.IsNullOrWhiteSpace(href))
            {
                AppLog.Data(
                    $"skipped hero card due to missing href;position={position}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"query={query};position={position};profileName={visibleName}");
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

            discovered[absoluteHref] = new ContactDiscoveryTarget(absoluteHref, visibleName, absoluteHref);
            added++;
            AppLog.Data(
                $"added hero card target;name={visibleName};url={absoluteHref}",
                "ProfileDiscovery",
                "capture-visible-targets",
                $"query={query};profileName={visibleName};profileUrl={absoluteHref};position={position}");
        }

        return added;
    }

    private static async Task<ILocator?> FindHeroCardContainerAsync(IPage page, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.HeroCardContainerCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = page.Locator(selector).First;
            if (await candidate.IsVisibleAsync())
            {
                AppLog.Data(
                    $"heroCardContainerSelector={selector}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"heroCardContainerSelector={selector}");
                return candidate;
            }
        }

        return null;
    }

    private static async Task<string?> ResolveHeroCardSelectorAsync(ILocator container, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.HeroCardCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await container.Locator(selector).CountAsync();
            if (count > 0)
            {
                return selector;
            }
        }

        return null;
    }

    private static async Task<string?> ExtractCardNameAsync(ILocator card, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.HeroCardNameCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = card.Locator(selector).First;
            if (!await locator.IsVisibleAsync())
            {
                continue;
            }

            var text = (await locator.InnerTextAsync()).Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static async Task<string?> ExtractCardHrefAsync(ILocator card, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.HeroCardLinkCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = card.Locator(selector).First;
            var count = await card.Locator(selector).CountAsync();
            if (count == 0)
            {
                continue;
            }

            var href = await locator.GetAttributeAsync("href");
            if (!string.IsNullOrWhiteSpace(href))
            {
                return href.Trim();
            }
        }

        return null;
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
