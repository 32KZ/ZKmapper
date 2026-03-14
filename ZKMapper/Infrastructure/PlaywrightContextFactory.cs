using Microsoft.Playwright;

namespace ZKMapper.Infrastructure;

internal sealed class PlaywrightContextFactory
{
    public async Task<IBrowserContext> CreateAuthenticatedContextAsync(IBrowser browser)
    {
        return await CreateContextAsync(browser, includeStorageState: true);
    }

    public async Task<IBrowserContext> CreateContextAsync(IBrowser browser, bool includeStorageState)
    {
        using var timer = ExecutionTimer.Start("CreateBrowserContext");

        var options = new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width = 1280,
                Height = 800
            },
            TimezoneId = "Europe/London",
            Locale = "en-GB",
            StrictSelectors = true
        };

        if (includeStorageState && File.Exists(AppPaths.SessionStatePath))
        {
            AppLog.Session("session file found", "check-session-file", $"path={AppPaths.SessionStatePath}");
            AppLog.Session("loading storage state", "load-storage-state", $"path={AppPaths.SessionStatePath}");
            options.StorageStatePath = AppPaths.SessionStatePath;
        }
        else if (includeStorageState)
        {
            AppLog.Session("authentication required", "check-session-file", $"path={AppPaths.SessionStatePath}");
        }
        else
        {
            AppLog.Session("creating empty browser context", "create-context", "mode=unauthenticated");
        }

        AppLog.Data(
            "browser context configuration prepared",
            "CreateBrowserContext",
            "configure-context",
            $"viewport=1280x800;locale={options.Locale};timezone={options.TimezoneId};strictSelectors={options.StrictSelectors}");

        var context = await browser.NewContextAsync(options);
        AppLog.Result("browser context created", "CreateBrowserContext", "new-context");
        return context;
    }
}
