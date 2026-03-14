using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Serilog;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ProfileExtractionService
{
    private readonly RetryService _retryService;

    public ProfileExtractionService(RetryService retryService)
    {
        _retryService = retryService;
    }

    public async Task<IPage?> TryOpenProfileInNewTabAsync(
        IBrowserContext context,
        IPage resultsPage,
        ContactDiscoveryTarget target,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(target.Href))
        {
            return null;
        }

        Log.Information("Attempt to open a profile in a new tab for {ProfileUrl}", target.Href);

        try
        {
            var pageTask = context.WaitForPageAsync(new BrowserContextWaitForPageOptions
            {
                Timeout = 5000
            });

            await resultsPage.Locator($"a[href='{target.Href}']").First.ClickAsync(new LocatorClickOptions
            {
                Button = MouseButton.Left,
                Modifiers = new[] { KeyboardModifier.Control }
            });

            var newPage = await pageTask;
            await newPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

            Log.Information("Success opening profile in a new tab: {ProfileUrl}", newPage.Url);
            return newPage;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failure opening profile in a new tab for target {Target}", target.Href);
            return null;
        }
    }

    public async Task<ExtractedProfile> ExtractAsync(IPage profilePage, CancellationToken cancellationToken)
    {
        Log.Information("Start of profile extraction from {ProfileUrl}", profilePage.Url);

        var fullName = await _retryService.ExecuteAsync(
            token => ExtractFullNameAsync(profilePage, token),
            cancellationToken);

        var currentTitles = await _retryService.ExecuteAsync(
            token => ExtractCurrentRolesAsync(profilePage, token),
            cancellationToken);

        var extracted = new ExtractedProfile(fullName, profilePage.Url, currentTitles);
        Log.Information("Successful extraction for {FullName}", extracted.FullName);
        return extracted;
    }

    private static async Task<string> ExtractFullNameAsync(IPage page, CancellationToken cancellationToken)
    {
        var locator = await page.FirstVisibleAsync(LinkedInSelectors.ProfileHeaderNameCandidates, cancellationToken);
        var fullName = (await locator.InnerTextAsync()).Trim();
        return Regex.Replace(fullName, "\\s+", " ");
    }

    private static async Task<string> ExtractCurrentRolesAsync(IPage page, CancellationToken cancellationToken)
    {
        var section = await page.FirstVisibleOrNullAsync(LinkedInSelectors.ExperienceSectionCandidates, cancellationToken);
        if (section is null)
        {
            return string.Empty;
        }

        var itemTexts = await section.Locator("li, div").AllInnerTextsAsync();
        var currentRoles = itemTexts
            .Select(CleanText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Where(text =>
                text.Contains("present", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("current", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return string.Join("; ", currentRoles);
    }

    private static string CleanText(string value)
    {
        return Regex.Replace(value, "\\s+", " ").Trim();
    }
}
