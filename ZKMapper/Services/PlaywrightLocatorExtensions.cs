using Microsoft.Playwright;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal static class PlaywrightLocatorExtensions
{
    public static async Task<ILocator> FirstVisibleAsync(
        this IPage page,
        IEnumerable<string> selectors,
        CancellationToken cancellationToken)
    {
        AppLog.Trace(
            $"playwright selector queries={string.Join(" | ", selectors)}",
            "SelectorLookup",
            "find-first-visible",
            $"selectorCount={selectors.Count()}");

        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector).First;
            AppLog.Trace($"query selector {selector}", "SelectorLookup", "query-selector", $"selector={selector}");
            if (await IsVisibleWithinAsync(locator))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppLog.Result($"selector resolved: {selector}", "SelectorLookup", "find-first-visible", $"selector={selector}");
                return locator;
            }
        }

        await PlaywrightDiagnostics.LogSelectorFailureAsync(page, selectors, "SelectorLookup", cancellationToken);
        throw new InvalidOperationException($"None of the selectors resolved to a visible element: {string.Join(", ", selectors)}");
    }

    public static async Task<ILocator?> FirstVisibleOrNullAsync(
        this IPage page,
        IEnumerable<string> selectors,
        CancellationToken cancellationToken)
    {
        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector).First;
            AppLog.Trace($"query selector {selector}", "SelectorLookup", "query-selector", $"selector={selector}");
            if (await IsVisibleWithinAsync(locator))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AppLog.Result($"selector resolved: {selector}", "SelectorLookup", "find-first-visible-or-null", $"selector={selector}");
                return locator;
            }
        }

        await PlaywrightDiagnostics.LogSelectorFailureAsync(page, selectors, "SelectorLookup", cancellationToken);
        return null;
    }

    private static async Task<bool> IsVisibleWithinAsync(ILocator locator)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 1500
            });
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }
}
