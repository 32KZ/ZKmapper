using System.Text.Json;
using System.Text.Json.Serialization;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private AppSettings _settings;

    public ConfigurationService()
    {
        _settings = Load();
    }

    public AppSettings Current => _settings;

    public DelayRange GetDelayRange(DelayProfile profile)
    {
        return profile switch
        {
            DelayProfile.Navigation => new DelayRange(_settings.Delays.NavigationMinMs, _settings.Delays.NavigationMaxMs),
            DelayProfile.Scroll => new DelayRange(_settings.Delays.ScrollMinMs, _settings.Delays.ScrollMaxMs),
            DelayProfile.Profile => new DelayRange(_settings.Delays.ProfileMinMs, _settings.Delays.ProfileMaxMs),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown delay profile.")
        };
    }

    public WebhookSettings GetWebhookSettings()
    {
        return new WebhookSettings
        {
            ActiveMode = _settings.Webhook.ActiveMode,
            ProductionWebhookUrl = _settings.Webhook.ProductionWebhookUrl,
            TestWebhookUrl = _settings.Webhook.TestWebhookUrl,
            HeaderAuthenticationEnabled = _settings.Webhook.HeaderAuthenticationEnabled
        };
    }

    public void UpdateDelayRange(DelayProfile profile, DelayRange range)
    {
        range.Validate();

        switch (profile)
        {
            case DelayProfile.Navigation:
                _settings.Delays.NavigationMinMs = range.MinMs;
                _settings.Delays.NavigationMaxMs = range.MaxMs;
                break;
            case DelayProfile.Scroll:
                _settings.Delays.ScrollMinMs = range.MinMs;
                _settings.Delays.ScrollMaxMs = range.MaxMs;
                break;
            case DelayProfile.Profile:
                _settings.Delays.ProfileMinMs = range.MinMs;
                _settings.Delays.ProfileMaxMs = range.MaxMs;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown delay profile.");
        }

        Save();
        AppLog.Info("[CONFIG] delay updated", "Configuration", "update-delay-range", $"profile={profile};minMs={range.MinMs};maxMs={range.MaxMs}");
    }

    public void UpdateWebhookMode(WebhookMode mode)
    {
        _settings.Webhook.ActiveMode = mode;
        Save();
        AppLog.Info("[CONFIG] webhook mode updated", "Configuration", "update-webhook-mode", $"mode={mode}");
    }

    public void UpdateWebhookUrl(WebhookMode mode, string url)
    {
        var sanitizedUrl = url.Trim();

        if (mode == WebhookMode.Production)
        {
            _settings.Webhook.ProductionWebhookUrl = sanitizedUrl;
        }
        else
        {
            _settings.Webhook.TestWebhookUrl = sanitizedUrl;
        }

        Save();
        AppLog.Info("[CONFIG] webhook url updated", "Configuration", "update-webhook-url", $"mode={mode};configured={!string.IsNullOrWhiteSpace(sanitizedUrl)}");
    }

    public void UpdateHeaderAuthenticationEnabled(bool enabled)
    {
        _settings.Webhook.HeaderAuthenticationEnabled = enabled;
        Save();
        AppLog.Info("[CONFIG] webhook header auth updated", "Configuration", "update-webhook-header-auth", $"enabled={enabled}");
    }

    private AppSettings Load()
    {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);

        if (!File.Exists(AppPaths.SettingsFilePath))
        {
            var defaults = new AppSettings();
            Validate(defaults);
            Save(defaults);
            AppLog.Info("[CONFIG] configuration loaded", "Configuration", "load-config", $"path={AppPaths.SettingsFilePath};createdDefault=true");
            return defaults;
        }

        var json = File.ReadAllText(AppPaths.SettingsFilePath);
        var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        Validate(settings);
        Save(settings);
        AppLog.Info("[CONFIG] configuration loaded", "Configuration", "load-config", $"path={AppPaths.SettingsFilePath};createdDefault=false");
        return settings;
    }

    private void Save()
    {
        Save(_settings);
    }

    private static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppPaths.SettingsFilePath, json);
    }

    private static void Validate(AppSettings settings)
    {
        new DelayRange(settings.Delays.NavigationMinMs, settings.Delays.NavigationMaxMs).Validate();
        new DelayRange(settings.Delays.ScrollMinMs, settings.Delays.ScrollMaxMs).Validate();
        new DelayRange(settings.Delays.ProfileMinMs, settings.Delays.ProfileMaxMs).Validate();
        settings.Webhook ??= new WebhookSettings();
        settings.Webhook.ProductionWebhookUrl ??= string.Empty;
        settings.Webhook.TestWebhookUrl ??= string.Empty;
    }
}
