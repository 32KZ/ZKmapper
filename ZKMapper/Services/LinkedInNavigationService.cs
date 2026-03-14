using Microsoft.Playwright;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class LinkedInNavigationService
{
    private readonly RetryService _retryService;
    private readonly HumanDelayService _humanDelayService;

    public LinkedInNavigationService(RetryService retryService, HumanDelayService humanDelayService)
    {
        _retryService = retryService;
        _humanDelayService = humanDelayService;
    }

    public async Task NavigateToCompanyPeoplePageAsync(
        IPage page,
        CompanyInput input,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("NavigateToCompanyPeoplePage");
        AppLog.Step("Opening company page", "CompanyNavigation", "goto-company-page", $"url={input.CompanyLinkedInUrl}");
        AppLog.Data($"url={input.CompanyLinkedInUrl}", "CompanyNavigation", "goto-company-page", $"company={input.CompanyName};url={input.CompanyLinkedInUrl}");
        AppLog.Action("navigating browser", "CompanyNavigation", "goto-company-page", $"url={input.CompanyLinkedInUrl}");

        await _retryService.ExecuteAsync(async token =>
        {
            await page.GotoAsync(input.CompanyLinkedInUrl, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 30000
            });
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            token.ThrowIfCancellationRequested();
        }, cancellationToken);

        AppLog.Result("page loaded", "CompanyNavigation", "goto-company-page", $"company={input.CompanyName};url={page.Url}");
        await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "CompanyNavigation", "goto-company-page", cancellationToken);
        AppLog.Next("executing people tab selection", "CompanyNavigation", "select-people-tab", $"company={input.CompanyName}");
        await _humanDelayService.DelayAsync(2, 4, "stabilize company page before selecting People tab", cancellationToken);

        await EnsurePeopleTabSelectedAsync(page, cancellationToken);

        await page.FirstVisibleAsync(LinkedInSelectors.PeopleSearchInputCandidates, cancellationToken);
        AppLog.Result("People page ready", "CompanyNavigation", "select-people-tab", $"company={input.CompanyName};url={page.Url}");
        AppLog.Next("executing people search", "CompanyNavigation", "ready-for-query", $"company={input.CompanyName}");
        await _humanDelayService.DelayAsync(2, 4, "stabilize People page before query execution", cancellationToken);
    }

    private async Task EnsurePeopleTabSelectedAsync(IPage page, CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("EnsurePeopleTabSelected");
        await _retryService.ExecuteAsync(async token =>
        {
            var peopleTab = await page.FirstVisibleAsync(LinkedInSelectors.PeopleTabCandidates, token);
            AppLog.Trace(
                $"playwright selector queries={string.Join(" | ", LinkedInSelectors.PeopleTabCandidates)}",
                "CompanyNavigation",
                "resolve-people-tab",
                $"candidateCount={LinkedInSelectors.PeopleTabCandidates.Length}");
            AppLog.Action("click", "CompanyNavigation", "click-people-tab", $"selectorCandidates={string.Join(" | ", LinkedInSelectors.PeopleTabCandidates)}");
            await peopleTab.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var onPeoplePage = page.Url.Contains("/people/", StringComparison.OrdinalIgnoreCase) ||
                await page.FirstVisibleOrNullAsync(LinkedInSelectors.PeopleSearchInputCandidates, token) is not null;

            if (!onPeoplePage)
            {
                await PlaywrightDiagnostics.TracePageSnapshotAsync(page, "CompanyNavigation", "click-people-tab", token);
                throw new InvalidOperationException("People tab click did not navigate to the People page.");
            }

            AppLog.Result("People tab selected", "CompanyNavigation", "click-people-tab", $"url={page.Url}");
            token.ThrowIfCancellationRequested();
        }, cancellationToken);
    }
}
