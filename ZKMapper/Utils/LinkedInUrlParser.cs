using ZKMapper.Infrastructure;

namespace ZKMapper.Utils;

internal static class LinkedInUrlParser
{
    public static string ExtractCompanySlug(string companyUrl)
    {
        using var timer = ExecutionTimer.Start("CompanySlugParsing");
        AppLog.Step("extracting company slug", "CompanySlugParsing", "extract-company-slug");
        AppLog.Data($"inputUrl={companyUrl}", "CompanySlugParsing", "extract-company-slug", $"inputUrl={companyUrl}");

        if (string.IsNullOrWhiteSpace(companyUrl))
        {
            throw new InvalidOperationException("Company URL is required.");
        }

        var normalizedUrl = companyUrl.Trim();
        if (!normalizedUrl.Contains("://", StringComparison.Ordinal))
        {
            normalizedUrl = $"https://{normalizedUrl}";
        }

        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid LinkedIn company URL: {companyUrl}");
        }

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var companyIndex = Array.FindIndex(segments, segment => string.Equals(segment, "company", StringComparison.OrdinalIgnoreCase));
        if (companyIndex < 0 || companyIndex + 1 >= segments.Length)
        {
            throw new InvalidOperationException($"LinkedIn company slug could not be derived from URL: {companyUrl}");
        }

        var slug = segments[companyIndex + 1].Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException($"LinkedIn company slug could not be derived from URL: {companyUrl}");
        }

        AppLog.Result($"slug={slug}", "CompanySlugParsing", "extract-company-slug", $"slug={slug}");
        return slug;
    }
}
