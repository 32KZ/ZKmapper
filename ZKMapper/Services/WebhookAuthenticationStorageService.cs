using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class WebhookAuthenticationStorageService
{
    public WebhookAuthenticationValues Load()
    {
        Directory.CreateDirectory(AppPaths.WebhookAuthenticationDirectory);

        var headerName = File.Exists(AppPaths.WebhookHeaderNamePath)
            ? File.ReadAllText(AppPaths.WebhookHeaderNamePath).Trim()
            : string.Empty;
        var headerSecret = File.Exists(AppPaths.WebhookHeaderSecretPath)
            ? File.ReadAllText(AppPaths.WebhookHeaderSecretPath).Trim()
            : string.Empty;

        return new WebhookAuthenticationValues(headerName, headerSecret);
    }

    public void Save(string headerName, string headerSecret)
    {
        Directory.CreateDirectory(AppPaths.WebhookAuthenticationDirectory);
        File.WriteAllText(AppPaths.WebhookHeaderNamePath, headerName.Trim());
        File.WriteAllText(AppPaths.WebhookHeaderSecretPath, headerSecret.Trim());
        AppLog.Info("[WEBHOOK] authentication values saved locally", "WebhookAuth", "save-auth", $"headerNameConfigured={!string.IsNullOrWhiteSpace(headerName)};headerSecretConfigured={!string.IsNullOrWhiteSpace(headerSecret)}");
    }
}
