using Microsoft.Playwright;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class LinkedInNavigationService
{
    private readonly HumanDelayService _humanDelayService;

    public LinkedInNavigationService(RetryService retryService, HumanDelayService humanDelayService)
    {
        _humanDelayService = humanDelayService;
    }

    public async Task NavigateToCompanyPeoplePageAsync(
        IPage page,
        CompanyInput input,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("CompanyNavigation");
        AppLog.Step("Opening company page", "CompanyNavigation", "goto-company-page", $"url={input.CompanyLinkedInUrl}");
        AppLog.Data($"url={input.CompanyLinkedInUrl}", "CompanyNavigation", "goto-company-page", $"company={input.CompanyName};url={input.CompanyLinkedInUrl}");
        AppLog.Action("navigating browser", "CompanyNavigation", "goto-company-page", $"url={input.CompanyLinkedInUrl}");

        await NavigateToCompanyPageAsync(page, input.CompanyLinkedInUrl, cancellationToken);
        await WaitForCompanyUiReadyAsync(page, cancellationToken);
        await ClickPeopleTabAsync(page, cancellationToken);
        await page.FirstVisibleAsync(LinkedInSelectors.PeopleSearchInputCandidates, cancellationToken);
        AppLog.Result("People page ready", "CompanyNavigation", "select-people-tab", $"company={input.CompanyName};url={page.Url}");
        AppLog.Next("executing people search", "CompanyNavigation", "ready-for-query", $"company={input.CompanyName}");
        await _humanDelayService.DelayAsync(2, 4, "stabilize People page before query execution", cancellationToken);
    }

    private static async Task NavigateToCompanyPageAsync(IPage page, string url, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                    Timeout = 60000
                });

                AppLog.Result("DOMContentLoaded reached", "CompanyNavigation", "goto-company-page", $"url={page.Url}");
                AppLog.Data($"navigationResultUrl={page.Url}", "CompanyNavigation", "goto-company-page", $"url={page.Url}");
                AppLog.Data($"Page title: {await page.TitleAsync()}", "CompanyNavigation", "goto-company-page", $"title={await page.TitleAsync()}");
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts)
            {
                var html = await page.ContentAsync();
                AppLog.Warn(
                    $"navigation retry attempt={attempt} url={url}",
                    "CompanyNavigation",
                    "goto-company-page",
                    $"attempt={attempt};url={url};domLength={html.Length};reason={ex.Message}");
                AppLog.Error(ex, $"navigation failed domLength={html.Length}", "CompanyNavigation", "goto-company-page", $"attempt={attempt};url={url};domLength={html.Length}");
                cancellationToken.ThrowIfCancellationRequested();
            }
        }

        var finalHtml = await page.ContentAsync();
        AppLog.Error(
            new InvalidOperationException("Navigation failed after retries."),
            $"navigation failed domLength={finalHtml.Length}",
            "CompanyNavigation",
            "goto-company-page",
            $"url={url};domLength={finalHtml.Length}");
        throw new InvalidOperationException($"Failed to navigate to company page: {url}");
    }

    private static async Task WaitForCompanyUiReadyAsync(IPage page, CancellationToken cancellationToken)
    {
        AppLog.Step("locating People tab", "CompanyNavigation", "wait-for-people-selector", "locator=role-link:People");

        var peopleTabCandidates = page.GetByRole(AriaRole.Link, new() { Name = "People" });
        var roleLocatorReady = await WaitForVisibleLocatorAsync(peopleTabCandidates.First, cancellationToken);

        if (!roleLocatorReady)
        {
            AppLog.Warn("role locator failed, using fallback selector", "CompanyNavigation", "wait-for-people-selector", "fallbackSelector=a[href$='/people/']");
            var fallbackHtml = await page.ContentAsync();
            AppLog.Trace($"domLength={fallbackHtml.Length}", "CompanyNavigation", "wait-for-people-selector", $"domLength={fallbackHtml.Length}");

            var visibleLinks = await page.GetByRole(AriaRole.Link).AllInnerTextsAsync();
            AppLog.Trace($"visibleLinks={string.Join(",", visibleLinks)}", "CompanyNavigation", "wait-for-people-selector", $"visibleLinkCount={visibleLinks.Count}");

            var fallback = page.Locator("a[href$='/people/']").First;
            var fallbackReady = await WaitForVisibleLocatorAsync(fallback, cancellationToken);

            if (!fallbackReady)
            {
                await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "CompanyNavigation", "wait-for-people-selector", cancellationToken);
                throw new InvalidOperationException("Unable to locate LinkedIn People tab using role-based or fallback selectors.");
            }
        }

        var count = await peopleTabCandidates.CountAsync();
        AppLog.Data($"peopleTabCandidates={count}", "CompanyNavigation", "wait-for-people-selector", $"peopleTabCandidates={count}");
        AppLog.Result("company page UI ready", "CompanyNavigation", "wait-for-people-selector", $"url={page.Url}");

        var html = await page.ContentAsync();
        AppLog.Data($"domLength={html.Length}", "CompanyNavigation", "wait-for-people-selector", $"domLength={html.Length}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "CompanyNavigation", "wait-for-people-selector", cancellationToken);
    }

    private static async Task ClickPeopleTabAsync(IPage page, CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ClickPeopleTab");
        AppLog.Step("locating People tab", "CompanyNavigation", "click-people-tab", "locator=role-link:People");

        var roleLocator = page.GetByRole(AriaRole.Link, new() { Name = "People" });
        var count = await roleLocator.CountAsync();
        AppLog.Data($"peopleTabCandidates={count}", "CompanyNavigation", "click-people-tab", $"peopleTabCandidates={count}");

        ILocator peopleLink;

        if (count > 0)
        {
            if (count > 1)
            {
                AppLog.Warn("multiple People tab candidates detected, selecting first", "CompanyNavigation", "click-people-tab", $"peopleTabCandidates={count}");
            }

            peopleLink = roleLocator.First;
        }
        else
        {
            AppLog.Warn("role locator failed, using fallback selector", "CompanyNavigation", "click-people-tab", "fallbackSelector=a[href$='/people/']");

            var html = await page.ContentAsync();
            AppLog.Trace($"domLength={html.Length}", "CompanyNavigation", "click-people-tab", $"domLength={html.Length}");

            var visibleLinks = await page.GetByRole(AriaRole.Link).AllInnerTextsAsync();
            AppLog.Trace($"visibleLinks={string.Join(",", visibleLinks)}", "CompanyNavigation", "click-people-tab", $"visibleLinkCount={visibleLinks.Count}");

            peopleLink = page.Locator("a[href$='/people/']").First;
        }

        AppLog.Action("clicking People tab", "CompanyNavigation", "click-people-tab", $"candidateCount={count}");
        await peopleLink.ClickAsync();
        AppLog.Result("People tab clicked", "CompanyNavigation", "click-people-tab", $"currentUrl={page.Url}");
        await page.WaitForURLAsync("**/people/**", new PageWaitForURLOptions
        {
            Timeout = 15000,
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Result("switched to People page", "CompanyNavigation", "click-people-tab", $"currentUrl={page.Url}");
        AppLog.Data($"currentUrl={page.Url}", "CompanyNavigation", "click-people-tab", $"currentUrl={page.Url}");
    }

    private static async Task<bool> WaitForVisibleLocatorAsync(ILocator locator, CancellationToken cancellationToken)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                Timeout = 15000,
                State = WaitForSelectorState.Visible
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
