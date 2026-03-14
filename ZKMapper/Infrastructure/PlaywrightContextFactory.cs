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
            options.StorageStatePath = AppPaths.SessionStatePath;
        }

        return await browser.NewContextAsync(options);
    }
}
