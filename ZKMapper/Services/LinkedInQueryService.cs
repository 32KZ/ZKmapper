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
        var searchInput = await LocatePeopleSearchInputAsync(page, cancellationToken);

        await _humanDelayService.DelayAsync(5, 12, "simulate human search interaction", cancellationToken);

        await _retryService.ExecuteAsync(async token =>
        {
            AppLog.Action("focusing People page search input", "QueryExecution", "focus-search-input", $"selectorScope=main;selectorCandidates={string.Join(" | ", LinkedInSelectors.PeopleSearchInputCandidates)}");
            await searchInput.ClickAsync();
            await searchInput.FillAsync(string.Empty);
            await searchInput.PressAsync("Control+A");
            await searchInput.PressAsync("Backspace");

            AppLog.Action("entering query text", "QueryExecution", "enter-query-text", $"query={query}");
            AppLog.Data($"query=\"{query}\"", "QueryExecution", "enter-query-text", $"query={query}");
            await searchInput.PressSequentiallyAsync(query, new LocatorPressSequentiallyOptions { Delay = 85 });

            AppLog.Action("submitting search query", "QueryExecution", "submit-query", $"query={query}");
            await searchInput.PressAsync("Enter");
            token.ThrowIfCancellationRequested();
        }, cancellationToken);

        AppLog.Step("waiting for filtered people results", "QueryExecution", "wait-for-results", "selector=ul[role='list']");
        var resultsContainer = page.Locator("ul[role='list']").First;
        await _retryService.ExecuteAsync(async token =>
        {
            await resultsContainer.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });
            await _humanDelayService.DelayAsync(5, 12, "allow filtered LinkedIn people results to render after query", token);
        }, cancellationToken);

        AppLog.Result("filtered results loaded", "QueryExecution", "wait-for-results", $"query={query};queryUrl={page.Url}");
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

    private static async Task<ILocator> LocatePeopleSearchInputAsync(IPage page, CancellationToken cancellationToken)
    {
        AppLog.Step("locating People page search input", "SelectorLookup", "locate-people-search-input", "selectorScope=main");
        AppLog.Data("selectorScope=main", "SelectorLookup", "locate-people-search-input", "selectorScope=main");
        AppLog.Trace(
            $"selectorCandidates={string.Join(", ", LinkedInSelectors.PeopleSearchInputCandidates)}",
            "SelectorLookup",
            "locate-people-search-input",
            $"selectorCount={LinkedInSelectors.PeopleSearchInputCandidates.Length}");

        var container = page.Locator("main").First;
        await container.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        var visibleInputCount = await container.Locator("input").CountAsync();
        AppLog.Trace($"visibleInputs={visibleInputCount}", "SelectorLookup", "locate-people-search-input", $"visibleInputs={visibleInputCount}");

        var placeholders = await container.Locator("input").EvaluateAllAsync<string[]>(
            @"elements => elements
                .map(element => element.getAttribute('placeholder'))
                .filter(value => value)");

        foreach (var placeholder in placeholders)
        {
            AppLog.Trace($"inputPlaceholder=\"{placeholder}\"", "SelectorLookup", "locate-people-search-input", $"inputPlaceholder={placeholder}");
        }

        foreach (var selector in LinkedInSelectors.PeopleSearchInputCandidates)
        {
            var scopedSelector = selector.StartsWith("main ", StringComparison.Ordinal)
                ? selector["main ".Length..]
                : selector;

            var locator = container.Locator(scopedSelector);
            var count = await locator.CountAsync();
            AppLog.Data($"searchInputCandidates={count}", "SelectorLookup", "locate-people-search-input", $"selector={selector};searchInputCandidates={count}");

            if (count == 0)
            {
                continue;
            }

            var visibleLocator = locator.First;
            if (await IsVisibleAsync(visibleLocator, cancellationToken))
            {
                AppLog.Result($"selector resolved {selector}", "SelectorLookup", "locate-people-search-input", $"selector={selector}");
                return visibleLocator;
            }
        }

        var html = await page.ContentAsync();
        var title = await page.TitleAsync();
        AppLog.Error(
            new InvalidOperationException("People page search input not found"),
            "People page search input not found",
            "SelectorLookup",
            "locate-people-search-input",
            $"url={page.Url};title={title};domLength={html.Length}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "SelectorLookup", "locate-people-search-input", cancellationToken);
        throw new InvalidOperationException("People page search input not found.");
    }

    private static async Task<bool> IsVisibleAsync(ILocator locator, CancellationToken cancellationToken)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 1500
            });
            cancellationToken.ThrowIfCancellationRequested();
            return true;
        }
        catch (TimeoutException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return false;
        }
    }
}
