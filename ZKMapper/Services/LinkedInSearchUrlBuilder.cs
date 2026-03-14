using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class LinkedInSearchUrlBuilder
{
    public string BuildSearchUrl(string companyId, string keyword)
    {
        using var timer = ExecutionTimer.Start("SearchUrlBuilder");
        AppLog.Step("building LinkedIn search URL", "SearchUrlBuilder", "build-search-url");
        AppLog.Data($"companyId={companyId}", "SearchUrlBuilder", "build-search-url", $"companyId={companyId}");
        AppLog.Data($"keyword={keyword}", "SearchUrlBuilder", "build-search-url", $"keyword={keyword}");

        var encodedKeyword = Uri.EscapeDataString(keyword);
        var searchUrl = $"https://www.linkedin.com/search/results/people/?currentCompany=%5B%22{companyId}%22%5D&keywords={encodedKeyword}&origin=FACETED_SEARCH";

        AppLog.Result($"searchUrl={searchUrl}", "SearchUrlBuilder", "build-search-url", $"companyId={companyId};keyword={keyword}");
        return searchUrl;
    }
}
