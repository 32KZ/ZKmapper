using Microsoft.Playwright;
using Serilog;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class SessionStateManager
{
    public bool SessionStateExists()
    {
        return File.Exists(AppPaths.SessionStatePath);
    }

    public void EnsureSessionStateExists()
    {
        if (!SessionStateExists())
        {
            Log.Error("LinkedIn auth state file is missing at {Path}", AppPaths.SessionStatePath);
            throw new InvalidOperationException(
                $"LinkedIn auth state is missing. Run `ZKMapper auth` first. Expected file: {AppPaths.SessionStatePath}");
        }

        Log.Information("Auth state loaded from {Path}", AppPaths.SessionStatePath);
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
