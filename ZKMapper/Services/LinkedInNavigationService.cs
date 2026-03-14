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
        AppLog.Step("waiting for People tab selector", "CompanyNavigation", "wait-for-people-selector", "selector=a[href*='people']");
        await page.WaitForSelectorAsync("a[href*='people']", new PageWaitForSelectorOptions
        {
            Timeout = 15000,
            State = WaitForSelectorState.Visible
        });

        AppLog.Result("selector found", "CompanyNavigation", "wait-for-people-selector", "selector=a[href*='people']");
        AppLog.Result("company page UI ready", "CompanyNavigation", "wait-for-people-selector", $"url={page.Url}");

        var html = await page.ContentAsync();
        AppLog.Data($"domLength={html.Length}", "CompanyNavigation", "wait-for-people-selector", $"domLength={html.Length}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "CompanyNavigation", "wait-for-people-selector", cancellationToken);
    }

    private static async Task ClickPeopleTabAsync(IPage page, CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ClickPeopleTab");
        AppLog.Action("clicking People tab", "CompanyNavigation", "click-people-tab", "selector=a[href*='people']");

        var peopleLink = page.Locator("a[href*='people']").First;
        await peopleLink.ClickAsync();
        await page.WaitForURLAsync("**/people/**", new PageWaitForURLOptions
        {
            Timeout = 15000,
            WaitUntil = WaitUntilState.DOMContentLoaded
        });

        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Result("switched to People tab", "CompanyNavigation", "click-people-tab", $"currentUrl={page.Url}");
        AppLog.Data($"currentUrl={page.Url}", "CompanyNavigation", "click-people-tab", $"currentUrl={page.Url}");
    }
}
