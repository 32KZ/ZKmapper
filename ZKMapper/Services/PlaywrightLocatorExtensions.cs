using Microsoft.Playwright;

namespace ZKMapper.Services;

internal static class PlaywrightLocatorExtensions
{
    public static async Task<ILocator> FirstVisibleAsync(
        this IPage page,
        IEnumerable<string> selectors,
        CancellationToken cancellationToken)
    {
        foreach (var selector in selectors)
        {
            var locator = page.Locator(selector).First;
            if (await locator.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 1500 }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return locator;
            }
        }

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
            if (await locator.IsVisibleAsync(new LocatorIsVisibleOptions { Timeout = 1500 }))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return locator;
            }
        }

        return null;
    }
}
