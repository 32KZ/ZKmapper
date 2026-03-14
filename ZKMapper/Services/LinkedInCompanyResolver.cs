using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class LinkedInCompanyResolver
{
    private static readonly Regex CompanyIdRegex = new("\"companyId\"\\s*:\\s*(\\d+)", RegexOptions.Compiled);

    public async Task<string> ExtractCompanyIdAsync(IPage page)
    {
        using var timer = ExecutionTimer.Start("CompanyResolution");
        AppLog.Step("extracting companyId from company page", "CompanyResolution", "extract-company-id", $"url={page.Url}");

        var html = await page.ContentAsync();
        var match = CompanyIdRegex.Match(html);
        if (!match.Success)
        {
            AppLog.Error(
                new InvalidOperationException("companyId not found"),
                "companyId not found on company page",
                "CompanyResolution",
                "extract-company-id",
                $"url={page.Url};domLength={html.Length}");
            throw new InvalidOperationException("companyId not found on company page.");
        }

        var companyId = match.Groups[1].Value;
        AppLog.Result("companyId extracted", "CompanyResolution", "extract-company-id", $"companyId={companyId}");
        AppLog.Data($"companyId={companyId}", "CompanyResolution", "extract-company-id", $"companyId={companyId}");
        return companyId;
    }
}
