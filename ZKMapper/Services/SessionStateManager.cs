using Microsoft.Playwright;
using Serilog;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class SessionStateManager
{
    public async Task CaptureSessionStateAsync(
        BrowserManager browserManager,
        ConsolePromptService promptService,
        CancellationToken cancellationToken)
    {
        await using var session = await browserManager.LaunchAsync(useSavedSession: false, cancellationToken);
        await session.Page.GotoAsync("https://www.linkedin.com/login", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout = 30000
        });

        promptService.WaitForEnter("Press ENTER once LinkedIn login is complete.");
        await SaveStorageStateAsync(session.Context, cancellationToken);
    }

    public bool SessionStateExists()
    {
        return File.Exists(AppPaths.SessionStatePath);
    }

    public void EnsureSessionStateExists()
    {
        if (!SessionStateExists())
        {
            Log.Error(
                "LinkedIn session state file is missing at {Path}. Run `dotnet run -- auth` to create it.",
                AppPaths.SessionStatePath);
            throw new InvalidOperationException(
                $"LinkedIn auth state is missing. Run `dotnet run -- auth` first. Expected file: {AppPaths.SessionStatePath}");
        }

        Log.Information("Session state loaded from {Path}", AppPaths.SessionStatePath);
    }

    public async Task SaveStorageStateAsync(IBrowserContext context, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppPaths.SessionDirectory);
        await context.StorageStateAsync(new BrowserContextStorageStateOptions
        {
            Path = AppPaths.SessionStatePath
        });

        cancellationToken.ThrowIfCancellationRequested();
        Log.Information("Saved LinkedIn auth state to {Path}", AppPaths.SessionStatePath);
    }
}
