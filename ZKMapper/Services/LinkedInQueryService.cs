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
        cancellationToken.ThrowIfCancellationRequested();

        ILocator? scope = null;
        var scopeSource = "none";

        scope = await page.FirstVisibleOrNullAsync(
            LinkedInSelectors.HeroCardContainerCandidates,
            cancellationToken);

        if (scope is not null)
        {
            scopeSource = "hero-card-container";
        }
        else
        {
            scope = await page.FirstVisibleOrNullAsync(
                LinkedInSelectors.ResultsContainerCandidates,
                cancellationToken);

            if (scope is not null)
            {
                scopeSource = "results-container";
            }
            else
            {
                var mainLocator = page.Locator("main").First;
                if (await mainLocator.CountAsync() > 0)
                {
                    scope = mainLocator;
                    scopeSource = "main";
                }
                else
                {
                    scope = page.Locator("body").First;
                    scopeSource = "body";
                }
            }
        }

        AppLog.Data(
            $"hero-card-scope={scopeSource}",
            "ProfileDiscovery",
            "capture-visible-targets",
            $"query={query};scope={scopeSource}");

        var cards = scope.Locator(string.Join(", ", LinkedInSelectors.HeroCardCandidates));
        var count = await cards.CountAsync();
        AppLog.Data(
            $"heroCardCount={count}",
            "ProfileDiscovery",
            "capture-visible-targets",
            $"query={query};scope={scopeSource}");

        var added = 0;
        var skippedAnonymous = 0;
        var skippedBlank = 0;
        var skippedMissingHref = 0;
        var skippedDuplicate = 0;

        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var card = cards.Nth(index);
            var position = index + 1;
            var displayName = await ExtractCardNameAsync(card, cancellationToken);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                skippedBlank++;
                AppLog.Data(
                    $"skipped hero card due to blank name;position={position}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"query={query};position={position};reason=blank-name");
                continue;
            }

            if (displayName.Equals("LinkedIn Member", StringComparison.OrdinalIgnoreCase))
            {
                skippedAnonymous++;
                AppLog.Data(
                    $"skipped hero card due to anonymous name;position={position}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"query={query};position={position};reason=linkedin-member");
                continue;
            }

            var href = await ExtractCardHrefAsync(card, cancellationToken);

            if (string.IsNullOrWhiteSpace(href))
            {
                skippedMissingHref++;
                AppLog.Data(
                    $"skipped hero card due to missing href;position={position}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"query={query};position={position};reason=missing-href");
                continue;
            }

            var absoluteHref = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? href
                : $"https://www.linkedin.com{href}";
            absoluteHref = absoluteHref.Split('?', 2)[0];

            if (!absoluteHref.Contains("/in/", StringComparison.OrdinalIgnoreCase))
            {
                skippedMissingHref++;
                AppLog.Data(
                    $"skipped hero card due to non-profile href;position={position}",
                    "ProfileDiscovery",
                    "capture-visible-targets",
                    $"query={query};position={position};reason=non-profile-href;href={absoluteHref}");
                continue;
            }

            if (discovered.ContainsKey(absoluteHref))
            {
                skippedDuplicate++;
                continue;
            }

            discovered[absoluteHref] = new ContactDiscoveryTarget(absoluteHref, displayName, absoluteHref);
            added++;
            AppLog.Data(
                $"added hero card target;name={displayName};url={absoluteHref};position={position}",
                "ProfileDiscovery",
                "capture-visible-targets",
                $"query={query};profileName={displayName};profileUrl={absoluteHref};position={position}");
        }

        AppLog.Data(
            $"hero-card-summary added={added};skippedAnonymous={skippedAnonymous};skippedBlank={skippedBlank};skippedMissingHref={skippedMissingHref};skippedDuplicate={skippedDuplicate}",
            "ProfileDiscovery",
            "capture-visible-targets",
            $"query={query};scope={scopeSource};added={added};skippedAnonymous={skippedAnonymous};skippedBlank={skippedBlank};skippedMissingHref={skippedMissingHref};skippedDuplicate={skippedDuplicate}");
        AppLog.Data(
            $"validTargets={added}",
            "ProfileDiscovery",
            "capture-visible-targets",
            $"query={query};validTargets={added};skippedAnonymous={skippedAnonymous};skippedBlank={skippedBlank}");

        return added;
    }

    private static async Task<string?> ExtractCardNameAsync(ILocator card, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.HeroCardNameCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = card.Locator(selector).First;
            if (await card.Locator(selector).CountAsync() == 0)
            {
                continue;
            }

            var raw = (await locator.InnerTextAsync())?.Trim();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            if (raw.Equals("LinkedIn Member", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (raw.Length < 3)
            {
                continue;
            }

            return raw;
        }

        return null;
    }

    private static async Task<string?> ExtractCardHrefAsync(ILocator card, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.HeroCardLinkCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = card.Locator(selector).First;
            if (await card.Locator(selector).CountAsync() == 0)
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
