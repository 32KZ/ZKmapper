using Microsoft.Playwright;
using Serilog;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class BrowserManager
{
    public async Task<PlaywrightSession> LaunchAsync(bool useSavedSession, CancellationToken cancellationToken)
    {
        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 350
        });

        var contextOptions = new BrowserNewContextOptions();
        if (useSavedSession)
        {
            contextOptions.StorageStatePath = AppPaths.SessionStatePath;
        }

        var context = await browser.NewContextAsync(contextOptions);
        var page = await context.NewPageAsync();

        cancellationToken.ThrowIfCancellationRequested();
        Log.Information("Browser launched in headed mode. Session state enabled: {Enabled}", useSavedSession);

        return new PlaywrightSession(playwright, browser, context, page);
    }
}

internal sealed class PlaywrightSession : IAsyncDisposable
{
    public PlaywrightSession(IPlaywright playwright, IBrowser browser, IBrowserContext context, IPage page)
    {
        Playwright = playwright;
        Browser = browser;
        Context = context;
        Page = page;
    }

    public IPlaywright Playwright { get; }
    public IBrowser Browser { get; }
    public IBrowserContext Context { get; }
    public IPage Page { get; }

    public async ValueTask DisposeAsync()
    {
        await Context.CloseAsync();
        await Browser.CloseAsync();
        Playwright.Dispose();
    }
}
