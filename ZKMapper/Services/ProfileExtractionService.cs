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
            AppLog.Action(
                "create new page",
                "ProfileOpen",
                "open-profile-tab",
                $"profileUrl={target.Href}");
            var profilePage = await context.NewPageAsync();

            AppLog.Action(
                "navigating browser",
                "ProfileOpen",
                "open-profile-tab",
                $"profileUrl={target.Href}");
            await profilePage.GotoAsync(target.Href, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = 60000
            });

            await profilePage.WaitForSelectorAsync("main", new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 15000
            });

            AppLog.Action("switching context", "ProfileOpen", "switch-profile-tab", $"profileUrl={target.Href}");
            await profilePage.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            await profilePage.BringToFrontAsync();
            AppLog.Result("profile tab context ready", "ProfileOpen", "switch-profile-tab", $"profileUrl={profilePage.Url}");
            await PlaywrightDiagnostics.TracePageSnapshotAsync(profilePage, "ProfileOpen", "open-profile-tab", cancellationToken);
            await _humanDelayService.DelayAsync(DelayProfile.Profile, "allow profile tab to finish rendering", cancellationToken);

            AppLog.Result("profile page loaded", "ProfileOpen", "open-profile-tab", $"profileUrl={profilePage.Url}");
            return profilePage;
        }
        catch (Exception ex)
        {
            await PlaywrightDiagnostics.TracePageSnapshotAsync(resultsPage, "ProfileOpen", "open-profile-tab", cancellationToken);
            AppLog.Warn(ex, $"Failure opening profile via direct navigation for target {target.Href}", "ProfileOpen", "open-profile-tab", $"profileUrl={target.Href}");
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
            await ResolveProfileHeaderAsync(profilePage, cancellationToken);

            var fullName = await _retryService.ExecuteAsync(
                token => ExtractFullNameAsync(profilePage, token),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(fullName))
            {
                await PlaywrightDiagnostics.TracePageSnapshotAsync(profilePage, "ProfileExtraction", "extract-full-name", cancellationToken);
                AppLog.Warn($"profileNameExtractionFailed url={profilePage.Url}", "ProfileExtraction", "extract-full-name", $"profileUrl={profilePage.Url}");
                return null;
            }

            AppLog.Data($"profileName={fullName}", "ProfileExtraction", "extract-full-name", $"profileUrl={profilePage.Url};profileName={fullName}");
            await _humanDelayService.DelayAsync(DelayProfile.Profile, "pause between profile field extraction steps", cancellationToken);

            var headline = await ExtractHeadlineAsync(profilePage, fullName, cancellationToken);
            if (string.IsNullOrWhiteSpace(headline))
            {
                AppLog.Warn("field not found: Headline", "ProfileExtraction", "extract-headline", $"profileUrl={profilePage.Url}");
            }
            else
            {
                AppLog.Data($"profileHeadline={headline}", "ProfileExtraction", "extract-headline", $"profileUrl={profilePage.Url};profileHeadline={headline}");
            }

            var currentTitle = await _retryService.ExecuteAsync(
                token => ExtractCurrentRoleAsync(profilePage, token),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(currentTitle))
            {
                AppLog.Warn("field not found: JobTitle", "ProfileExtraction", "extract-current-roles", $"profileUrl={profilePage.Url}");
            }
            else
            {
                AppLog.Data($"jobTitle={currentTitle}", "ProfileExtraction", "extract-current-roles", $"profileUrl={profilePage.Url};jobTitle={currentTitle}");
            }

            var extracted = new ExtractedProfile(
                fullName,
                headline,
                CanonicalizeLinkedInProfileUrl(profilePage.Url),
                currentTitle,
                DateTime.UtcNow,
                searchQuery);
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

    private static async Task ResolveProfileHeaderAsync(IPage page, CancellationToken cancellationToken)
    {
        AppLog.Step("waiting for profile page readiness", "ProfileExtraction", "wait-for-profile-ready", $"profileUrl={page.Url}");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await WaitForAnyVisibleAsync(page, LinkedInSelectors.ProfileShellCandidates, 15000, cancellationToken, "profile-shell");
        await WaitForAnyVisibleAsync(page, LinkedInSelectors.ProfileHeaderReadinessCandidates, 15000, cancellationToken, "profile-header");
        AppLog.Result("profile page ready", "ProfileExtraction", "wait-for-profile-ready", $"profileUrl={page.Url}");
    }

    private static async Task<string> ExtractFullNameAsync(IPage page, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.ProfileHeaderNameCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = page.Locator(selector);
            var count = await locator.CountAsync();
            for (var index = 0; index < count; index++)
            {
                var candidate = locator.Nth(index);
                if (!await candidate.IsVisibleAsync())
                {
                    continue;
                }

                var text = CleanProfileName(await candidate.InnerTextAsync());
                if (LooksLikeProfileName(text))
                {
                    return text;
                }
            }
        }

        foreach (var selector in LinkedInSelectors.ProfileTopCardCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = page.Locator(selector).First;
            if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
            {
                continue;
            }

            var text = await FindBestLineAsync(locator, LooksLikeProfileName, cancellationToken, CleanProfileName);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static async Task<string> ExtractHeadlineAsync(IPage page, string fullName, CancellationToken cancellationToken)
    {
        foreach (var selector in LinkedInSelectors.ProfileHeadlineCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = page.Locator(selector);
            var count = await locator.CountAsync();
            for (var index = 0; index < count; index++)
            {
                var candidate = locator.Nth(index);
                if (!await candidate.IsVisibleAsync())
                {
                    continue;
                }

                var text = CleanText(await candidate.InnerTextAsync());
                if (LooksLikeHeadline(text, fullName))
                {
                    return text;
                }
            }
        }

        foreach (var selector in LinkedInSelectors.ProfileTopCardCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var locator = page.Locator(selector).First;
            if (await locator.CountAsync() == 0 || !await locator.IsVisibleAsync())
            {
                continue;
            }

            var text = await FindBestLineAsync(locator, candidate => LooksLikeHeadline(candidate, fullName), cancellationToken, CleanText);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static async Task<string> ExtractCurrentRoleAsync(IPage page, CancellationToken cancellationToken)
    {
        foreach (var sectionSelector in LinkedInSelectors.ExperienceSectionCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = page.Locator(sectionSelector).First;
            if (await section.CountAsync() == 0 || !await section.IsVisibleAsync())
            {
                continue;
            }

            foreach (var titleSelector in LinkedInSelectors.CurrentExperienceTitleCandidates)
            {
                var title = await FindFirstMatchingTextAsync(section, titleSelector, LooksLikeExperienceTitle, cancellationToken);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }

            var fallback = await FindBestLineAsync(section, LooksLikeExperienceTitle, cancellationToken, CleanText);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }
        }

        return string.Empty;
    }

    private static async Task<string> FindFirstMatchingTextAsync(
        ILocator scope,
        string selector,
        Func<string, bool> predicate,
        CancellationToken cancellationToken)
    {
        var matches = scope.Locator(selector);
        var count = await matches.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = matches.Nth(index);
            if (!await candidate.IsVisibleAsync())
            {
                continue;
            }

            var text = CleanText(await candidate.InnerTextAsync());
            if (predicate(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static async Task<string> FindBestLineAsync(
        ILocator scope,
        Func<string, bool> predicate,
        CancellationToken cancellationToken,
        Func<string?, string> cleaner)
    {
        var raw = await scope.InnerTextAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        foreach (var line in raw.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = cleaner(line);
            if (predicate(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static async Task WaitForAnyVisibleAsync(
        IPage page,
        IEnumerable<string> selectors,
        int timeoutMs,
        CancellationToken cancellationToken,
        string readinessLabel)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? lastException = null;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var selector in selectors)
            {
                try
                {
                    await page.WaitForVisibleAsync(selector, 1500, cancellationToken);
                    AppLog.Result($"selector resolved: {selector}", "ProfileExtraction", "wait-for-profile-ready", $"profileUrl={page.Url};readiness={readinessLabel};selector={selector}");
                    return;
                }
                catch (Exception ex) when (ex is TimeoutException or PlaywrightException)
                {
                    lastException = ex;
                }
            }
        }

        await PlaywrightDiagnostics.LogSelectorFailureAsync(page, selectors, "ProfileExtraction", cancellationToken);
        throw new TimeoutException($"Timed out waiting for {readinessLabel} selectors. Last error: {lastException?.Message}");
    }

    private static bool LooksLikeProfileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("LinkedIn Member", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(value, @"\b\d+(st|nd|rd|th)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        if (value.Contains("followers", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("connections", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contact info", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("provides services", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Length < 3 || value.Length > 120)
        {
            return false;
        }

        return Regex.IsMatch(value, @"\p{L}");
    }

    private static bool LooksLikeHeadline(string value, string fullName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals(fullName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Contains("followers", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("connections", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contact info", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("location", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Experience", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Length >= 4;
    }

    private static bool LooksLikeExperienceTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("Experience", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Show all experiences", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(" at ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Contains("yr", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("mos", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("full-time", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("part-time", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contract", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("present", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(value, @"\b(19|20)\d{2}\b"))
        {
            return false;
        }

        return value.Length >= 3 && Regex.IsMatch(value, @"\p{L}");
    }

    private static string CleanText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Regex.Replace(value, "\\s+", " ").Trim();
    }

    private static string CleanProfileName(string? value)
    {
        var text = CleanText(value);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = Regex.Replace(text, @"\s*·\s*\d+(st|nd|rd|th)\b", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b(verified|open to work|hiring)\b", string.Empty, RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text;
    }

    private static string CanonicalizeLinkedInProfileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return url.Split('?', 2)[0].TrimEnd('/');
        }

        var canonical = $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}".TrimEnd('/');
        return canonical;
    }
}
