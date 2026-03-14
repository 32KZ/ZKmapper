using Microsoft.Playwright;
using Serilog;
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
        Log.Information("Company mapping started for {CompanyName}", input.CompanyName);
        Log.Information("Next step: navigate to company page {CompanyUrl}", input.CompanyLinkedInUrl);

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

        Log.Information("Company page loaded for {CompanyName}. Current URL: {CompanyUrl}", input.CompanyName, page.Url);
        await _humanDelayService.DelayAsync(2, 4, cancellationToken);

        Log.Information("Next step: select the People tab for {CompanyName}", input.CompanyName);
        await EnsurePeopleTabSelectedAsync(page, cancellationToken);

        await page.FirstVisibleAsync(LinkedInSelectors.PeopleSearchInputCandidates, cancellationToken);
        Log.Information("Entry into People page succeeded for {CompanyName}", input.CompanyName);
        await _humanDelayService.DelayAsync(2, 4, cancellationToken);
    }

    private async Task EnsurePeopleTabSelectedAsync(IPage page, CancellationToken cancellationToken)
    {
        await _retryService.ExecuteAsync(async token =>
        {
            var peopleTab = await page.FirstVisibleAsync(LinkedInSelectors.PeopleTabCandidates, token);
            await peopleTab.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            var onPeoplePage = page.Url.Contains("/people/", StringComparison.OrdinalIgnoreCase) ||
                await page.FirstVisibleOrNullAsync(LinkedInSelectors.PeopleSearchInputCandidates, token) is not null;

            if (!onPeoplePage)
            {
                throw new InvalidOperationException("People tab click did not navigate to the People page.");
            }

            token.ThrowIfCancellationRequested();
        }, cancellationToken);
    }
}
