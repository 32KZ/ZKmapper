using Microsoft.Playwright;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class SessionStateManager
{
    public async Task CaptureSessionStateAsync(
        BrowserManager browserManager,
        ConsolePromptService promptService,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("CaptureSessionState");
        AppLog.Session("checking for session file", "check-session-file", $"path={AppPaths.SessionStatePath}");
        await using var session = await browserManager.LaunchAsync(useSavedSession: false, cancellationToken);
        AppLog.Step("opening LinkedIn login page", "SessionHandling", "navigate-login-page", "url=https://www.linkedin.com/login");
        await session.Page.GotoAsync("https://www.linkedin.com/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30000
        });
        AppLog.Result("LinkedIn login page loaded", "SessionHandling", "navigate-login-page", $"url={session.Page.Url}");

        promptService.WaitForEnter("Press ENTER once LinkedIn login is complete.");
        await SaveStorageStateAsync(session.Context, cancellationToken);
    }

    public bool SessionStateExists()
    {
        return File.Exists(AppPaths.SessionStatePath);
    }

    public void EnsureSessionStateExists()
    {
        AppLog.Session("checking for session file", "check-session-file", $"path={AppPaths.SessionStatePath}");
        if (!SessionStateExists())
        {
            AppLog.Session("authentication required", "check-session-file", $"path={AppPaths.SessionStatePath}");
            throw new InvalidOperationException(
                $"LinkedIn auth state is missing. Run `dotnet run -- auth` first. Expected file: {AppPaths.SessionStatePath}");
        }

        AppLog.Session("session file found", "check-session-file", $"path={AppPaths.SessionStatePath}");
        AppLog.Result("session restored successfully", "SessionHandling", "check-session-file", $"path={AppPaths.SessionStatePath}");
    }

    public async Task SaveStorageStateAsync(IBrowserContext context, CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("SaveStorageState");
        Directory.CreateDirectory(AppPaths.SessionDirectory);
        await context.StorageStateAsync(new BrowserContextStorageStateOptions
        {
            Path = AppPaths.SessionStatePath
        });

        cancellationToken.ThrowIfCancellationRequested();
        AppLog.Result("Saved LinkedIn auth state", "SessionHandling", "save-storage-state", $"path={AppPaths.SessionStatePath}");
    }
}
