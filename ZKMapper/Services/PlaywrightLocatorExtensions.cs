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
            if (await IsVisibleWithinAsync(locator))
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
            if (await IsVisibleWithinAsync(locator))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return locator;
            }
        }

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
