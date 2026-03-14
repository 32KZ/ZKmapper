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

    public async Task NavigateToCompanyPageAsync(
        IPage page,
        CompanyInput input,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("CompanyNavigation");
        AppLog.Step("opening company page", "CompanyNavigation", "goto-company-page", $"url={input.CompanyLinkedInUrl}");
        AppLog.Data($"url={input.CompanyLinkedInUrl}", "CompanyNavigation", "goto-company-page", $"company={input.CompanyName};url={input.CompanyLinkedInUrl}");
        AppLog.Action("navigating browser", "CompanyNavigation", "goto-company-page", $"url={input.CompanyLinkedInUrl}");

        await page.GotoAsync(input.CompanyLinkedInUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 60000
        });

        await page.Locator("main").First.WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 15000
        });

        AppLog.Result("company page loaded", "CompanyNavigation", "goto-company-page", $"url={page.Url}");
        AppLog.Data($"url={page.Url}", "CompanyNavigation", "goto-company-page", $"company={input.CompanyName};url={page.Url}");
        await _humanDelayService.DelayAsync(DelayProfile.Navigation, "stabilize company page before companyId extraction", cancellationToken);
    }
}
