using System.Text.Json;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ConfigurationService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
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
            DelayProfile.Search => new DelayRange(_settings.Delays.SearchMinMs, _settings.Delays.SearchMaxMs),
            DelayProfile.ProfileOpen => new DelayRange(_settings.Delays.ProfileOpenMinMs, _settings.Delays.ProfileOpenMaxMs),
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown delay profile.")
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
            case DelayProfile.Search:
                _settings.Delays.SearchMinMs = range.MinMs;
                _settings.Delays.SearchMaxMs = range.MaxMs;
                break;
            case DelayProfile.ProfileOpen:
                _settings.Delays.ProfileOpenMinMs = range.MinMs;
                _settings.Delays.ProfileOpenMaxMs = range.MaxMs;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown delay profile.");
        }

        Save();
        AppLog.Info("[CONFIG] delay range updated", "Configuration", "update-delay-range", $"profile={profile};minMs={range.MinMs};maxMs={range.MaxMs}");
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
        new DelayRange(settings.Delays.SearchMinMs, settings.Delays.SearchMaxMs).Validate();
        new DelayRange(settings.Delays.ProfileOpenMinMs, settings.Delays.ProfileOpenMaxMs).Validate();
    }
}
