using Microsoft.Playwright;
using Serilog;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class BrowserManager
{
    private static readonly SemaphoreSlim InstallLock = new(1, 1);
    private static bool _playwrightInstalled;
    private readonly PlaywrightContextFactory _contextFactory;

    public BrowserManager(PlaywrightContextFactory contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<PlaywrightSession> LaunchAsync(bool useSavedSession, CancellationToken cancellationToken)
    {
        await EnsureChromiumInstalledAsync(cancellationToken);

        if (useSavedSession && !File.Exists(AppPaths.SessionStatePath))
        {
            Log.Error(
                "LinkedIn session state file is missing at {Path}. Run `dotnet run -- auth` before mapping.",
                AppPaths.SessionStatePath);
            throw new InvalidOperationException("LinkedIn session state is missing.");
        }

        var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = false,
            SlowMo = 50,
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled"
            }
        });

        var context = useSavedSession
            ? await _contextFactory.CreateAuthenticatedContextAsync(browser)
            : await _contextFactory.CreateContextAsync(browser, includeStorageState: false);
        var page = await context.NewPageAsync();

        cancellationToken.ThrowIfCancellationRequested();
        Log.Information("Browser launched in headed mode. Session state enabled: {Enabled}", useSavedSession);

        return new PlaywrightSession(playwright, browser, context, page);
    }

    private static async Task EnsureChromiumInstalledAsync(CancellationToken cancellationToken)
    {
        if (_playwrightInstalled)
        {
            return;
        }

        await InstallLock.WaitAsync(cancellationToken);
        try
        {
            if (_playwrightInstalled)
            {
                return;
            }

            Log.Information("Ensuring Playwright Chromium is installed");
            var exitCode = await Task.Run(() => Microsoft.Playwright.Program.Main(new[] { "install", "chromium" }), cancellationToken);
            if (exitCode != 0)
            {
                throw new InvalidOperationException($"Playwright install failed with exit code {exitCode}.");
            }

            _playwrightInstalled = true;
        }
        finally
        {
            InstallLock.Release();
        }
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
