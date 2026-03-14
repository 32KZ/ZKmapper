using Microsoft.Playwright;
using Serilog;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class LinkedInNavigationService
{
    private readonly RetryService _retryService;

    public LinkedInNavigationService(RetryService retryService)
    {
        _retryService = retryService;
    }

    public async Task NavigateToCompanyPeoplePageAsync(
        IPage page,
        CompanyInput input,
        CancellationToken cancellationToken)
    {
        Log.Information("Navigating to company page {CompanyUrl}", input.CompanyLinkedInUrl);

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

        await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);

        var peopleTab = await page.FirstVisibleAsync(LinkedInSelectors.PeopleTabCandidates, cancellationToken);
        await _retryService.ExecuteAsync(async token =>
        {
            await peopleTab.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            token.ThrowIfCancellationRequested();
        }, cancellationToken);

        Log.Information("Entry into People page succeeded for {CompanyName}", input.CompanyName);
        await Task.Delay(TimeSpan.FromSeconds(2.5), cancellationToken);
    }
}
