using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class LinkedInPeopleSearchUrlBuilder
{
    public string BuildPeopleSearchUrl(string slug, string keyword, string regionId)
    {
        using var timer = ExecutionTimer.Start("PeopleSearchUrlBuilder");
        AppLog.Step("building LinkedIn people search URL", "PeopleSearchUrlBuilder", "build-people-search-url");
        AppLog.Data($"slug={slug}", "PeopleSearchUrlBuilder", "build-people-search-url", $"slug={slug}");
        AppLog.Data($"keyword={keyword}", "PeopleSearchUrlBuilder", "build-people-search-url", $"keyword={keyword}");
        AppLog.Data($"regionId={regionId}", "PeopleSearchUrlBuilder", "build-people-search-url", $"regionId={regionId}");

        var encodedKeyword = Uri.EscapeDataString(keyword);
        var encodedRegionId = Uri.EscapeDataString(regionId);
        var url = $"https://www.linkedin.com/company/{slug}/people/?facetGeoRegion={encodedRegionId}&keywords={encodedKeyword}";

        AppLog.Result($"url={url}", "PeopleSearchUrlBuilder", "build-people-search-url", $"slug={slug};keyword={keyword};regionId={regionId}");
        return url;
    }
}
