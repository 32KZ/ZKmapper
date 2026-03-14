using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Serilog;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class ProfileExtractionService
{
    private readonly RetryService _retryService;
    private readonly HumanDelayService _humanDelayService;

    public ProfileExtractionService(RetryService retryService, HumanDelayService humanDelayService)
    {
        _retryService = retryService;
        _humanDelayService = humanDelayService;
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

        Log.Information("Attempting to open profile in new tab for {ProfileUrl}", target.Href);

        try
        {
            var originalUrl = resultsPage.Url;
            var pageTask = context.WaitForPageAsync(new BrowserContextWaitForPageOptions
            {
                Timeout = 5000
            });
            var link = resultsPage.Locator(LinkedInSelectors.BuildProfileLinkSelector(target.Href ?? string.Empty)).First;

            await link.ClickAsync(new LocatorClickOptions
            {
                Button = MouseButton.Left,
                Modifiers = new[] { KeyboardModifier.Control }
            });

            IPage newPage;
            try
            {
                newPage = await pageTask;
            }
            catch (TimeoutException)
            {
                if (!string.Equals(resultsPage.Url, originalUrl, StringComparison.OrdinalIgnoreCase))
                {
                    Log.Warning("Profile opened in the same tab and will be skipped: {ProfileUrl}", target.Href);
                    await resultsPage.GoBackAsync(new PageGoBackOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 15000
                    });
                    await resultsPage.FirstVisibleAsync(LinkedInSelectors.ResultsContainerCandidates, cancellationToken);
                }

                return null;
            }

            await newPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await _humanDelayService.DelayAsync(2, 4, cancellationToken);

            Log.Information("Profile opened in new tab: {ProfileUrl}", newPage.Url);
            return newPage;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failure opening profile in a new tab for target {Target}", target.Href);
            return null;
        }
    }

    public async Task<ExtractedProfile?> ExtractAsync(
        IPage profilePage,
        string searchQuery,
        CancellationToken cancellationToken)
    {
        Log.Information("Start of profile extraction from {ProfileUrl}", profilePage.Url);

        try
        {
            var fullName = await _retryService.ExecuteAsync(
                token => ExtractFullNameAsync(profilePage, token),
                cancellationToken);

            await _humanDelayService.DelayAsync(1, 2, cancellationToken);

            var currentTitles = await _retryService.ExecuteAsync(
                token => ExtractCurrentRolesAsync(profilePage, token),
                cancellationToken);

            var extracted = new ExtractedProfile(fullName, profilePage.Url, currentTitles, DateTime.UtcNow, searchQuery);
            Log.Information("Extraction success for {FullName}", extracted.FullName);
            return extracted;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Profile extraction failed: {ProfileUrl}", profilePage.Url);
            return null;
        }
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

        var itemTexts = await section.Locator(LinkedInSelectors.ExperienceItemSelector).AllInnerTextsAsync();
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
