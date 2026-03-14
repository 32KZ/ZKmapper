using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class LinkedInRegionMapper
{
    private static readonly Dictionary<string, string> RegionIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["germany"] = "101282230",
        ["united kingdom"] = "101165590",
        ["uk"] = "101165590",
        ["great britain"] = "101165590",
        ["united states"] = "103644278",
        ["united states of america"] = "103644278",
        ["usa"] = "103644278",
        ["us"] = "103644278"
    };

    public string ResolveRegionId(string country)
    {
        using var timer = ExecutionTimer.Start("RegionResolution");
        AppLog.Step("resolving LinkedIn region ID", "RegionResolution", "resolve-region-id");
        AppLog.Data($"country={country}", "RegionResolution", "resolve-region-id", $"country={country}");

        var normalized = country.Trim();
        if (RegionIds.TryGetValue(normalized, out var regionId))
        {
            AppLog.Result($"regionId={regionId}", "RegionResolution", "resolve-region-id", $"country={country};regionId={regionId}");
            return regionId;
        }

        AppLog.Error(
            new InvalidOperationException("LinkedIn region ID not configured"),
            "LinkedIn region ID not configured",
            "RegionResolution",
            "resolve-region-id",
            $"country={country}");
        throw new InvalidOperationException($"LinkedIn region ID not configured for country '{country}'.");
    }
}
