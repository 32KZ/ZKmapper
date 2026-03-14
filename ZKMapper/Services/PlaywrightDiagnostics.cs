using Microsoft.Playwright;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal static class PlaywrightDiagnostics
{
    public static async Task TracePageSnapshotAsync(IPage page, string step, string action, CancellationToken cancellationToken = default)
    {
        if (!AppLog.TraceEnabled)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var html = await page.ContentAsync();
        var domLength = html.Length;
        var preview = CreatePreview(html);

        AppLog.Trace($"raw DOM length={domLength}", step, action, $"domLength={domLength}");
        AppLog.Trace($"HTML snippet preview={preview}", step, action, $"htmlPreview={preview}");
    }

    public static async Task LogSelectorFailureAsync(IPage page, IEnumerable<string> selectors, string step, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selectorList = string.Join(" | ", selectors);
        var html = await page.ContentAsync();
        var preview = CreatePreview(html);

        AppLog.Warn(
            $"selector lookup failed for {selectorList}",
            step,
            "selector-failure",
            $"selector={selectorList};url={page.Url};htmlPreview={preview}");

        if (AppLog.TraceEnabled)
        {
            AppLog.Trace($"pageUrl={page.Url}", step, "selector-failure", $"pageUrl={page.Url}");
            AppLog.Trace($"raw DOM length={html.Length}", step, "selector-failure", $"domLength={html.Length}");
            AppLog.Trace($"HTML snippet preview={preview}", step, "selector-failure", $"htmlPreview={preview}");
        }
    }

    private static string CreatePreview(string html)
    {
        var preview = html.Replace(Environment.NewLine, " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Trim();

        return preview.Length > 240
            ? preview[..240]
            : preview;
    }
}
