using System.Text.RegularExpressions;
using Microsoft.Playwright;
using ZKMapper.Infrastructure;
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
            AppLog.Warn("Profile href is missing", "ProfileOpen", "validate-target", $"targetName={target.DisplayName}");
            return null;
        }

        using var timer = ExecutionTimer.Start("ProfileOpen");
        AppLog.Step("opening profile tab", "ProfileOpen", "open-profile-tab", $"profileUrl={target.Href}");

        try
        {
            var originalUrl = resultsPage.Url;
            var pageTask = context.WaitForPageAsync(new BrowserContextWaitForPageOptions
            {
                Timeout = 5000
            });
            var link = resultsPage.Locator(LinkedInSelectors.BuildProfileLinkSelector(target.Href ?? string.Empty)).First;

            AppLog.Action(
                "click",
                "ProfileOpen",
                "open-profile-tab",
                $"selector={LinkedInSelectors.BuildProfileLinkSelector(target.Href ?? string.Empty)};profileUrl={target.Href}");
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
                    AppLog.Warn("Profile opened in the same tab and will be skipped", "ProfileOpen", "open-profile-tab", $"profileUrl={target.Href};url={resultsPage.Url}");
                    await resultsPage.GoBackAsync(new PageGoBackOptions
                    {
                        WaitUntil = WaitUntilState.DOMContentLoaded,
                        Timeout = 15000
                    });
                    await resultsPage.FirstVisibleAsync(LinkedInSelectors.ResultsContainerCandidates, cancellationToken);
                }
                else
                {
                    await PlaywrightDiagnostics.TracePageSnapshotAsync(resultsPage, "ProfileOpen", "open-profile-tab", cancellationToken);
                }

                return null;
            }

            await newPage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await PlaywrightDiagnostics.TracePageSnapshotAsync(newPage, "ProfileOpen", "open-profile-tab", cancellationToken);
            await _humanDelayService.DelayAsync(DelayProfile.Profile, "allow profile tab to finish rendering", cancellationToken);

            AppLog.Result("profile page loaded", "ProfileOpen", "open-profile-tab", $"profileUrl={newPage.Url}");
            return newPage;
        }
        catch (Exception ex)
        {
            await PlaywrightDiagnostics.TracePageSnapshotAsync(resultsPage, "ProfileOpen", "open-profile-tab", cancellationToken);
            AppLog.Warn(ex, $"Failure opening profile in a new tab for target {target.Href}", "ProfileOpen", "open-profile-tab", $"profileUrl={target.Href}");
            return null;
        }
    }

    public async Task<ExtractedProfile?> ExtractAsync(
        IPage profilePage,
        string searchQuery,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ProfileExtraction");
        AppLog.Step("extracting profile details", "ProfileExtraction", "extract-profile", $"profileUrl={profilePage.Url}");

        try
        {
            var fullName = await _retryService.ExecuteAsync(
                token => ExtractFullNameAsync(profilePage, token),
                cancellationToken);

            AppLog.Data($"FullName={fullName}", "ProfileExtraction", "extract-full-name", $"profileUrl={profilePage.Url};FullName={fullName}");
            await _humanDelayService.DelayAsync(DelayProfile.Profile, "pause between profile field extraction steps", cancellationToken);

            var currentTitles = await _retryService.ExecuteAsync(
                token => ExtractCurrentRolesAsync(profilePage, token),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(currentTitles))
            {
                AppLog.Warn("field not found: JobTitle", "ProfileExtraction", "extract-current-roles", $"profileUrl={profilePage.Url}");
            }
            else
            {
                AppLog.Data($"JobTitle={currentTitles}", "ProfileExtraction", "extract-current-roles", $"profileUrl={profilePage.Url};JobTitle={currentTitles}");
            }

            var extracted = new ExtractedProfile(fullName, profilePage.Url, currentTitles, DateTime.UtcNow, searchQuery);
            AppLog.Data($"ProfileUrl={extracted.ProfileUrl}", "ProfileExtraction", "extract-profile", $"profileUrl={extracted.ProfileUrl}");
            AppLog.Result("profile extraction complete", "ProfileExtraction", "extract-profile", $"profileUrl={extracted.ProfileUrl}");
            return extracted;
        }
        catch (Exception ex)
        {
            await PlaywrightDiagnostics.TracePageSnapshotAsync(profilePage, "ProfileExtraction", "extract-profile", cancellationToken);
            AppLog.Warn(ex, $"profile extraction failed: {profilePage.Url}", "ProfileExtraction", "extract-profile", $"profileUrl={profilePage.Url};reason={ex.Message}");
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
