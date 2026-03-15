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

            await profilePage.WaitForSelectorAsync(LinkedInSelectors.MainContentSelector, new PageWaitForSelectorOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = 8000
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
        string discoveredName,
        string searchQuery,
        CancellationToken cancellationToken)
    {
        using var timer = ExecutionTimer.Start("ProfileExtraction");
        AppLog.Step("extracting profile details", "ProfileExtraction", "extract-profile", $"profileUrl={profilePage.Url}");

        try
        {
            await ResolveProfileExperienceAsync(profilePage, cancellationToken);

            var fullName = CleanProfileName(discoveredName);

            if (string.IsNullOrWhiteSpace(fullName))
            {
                AppLog.Warn($"profileNameUnavailable url={profilePage.Url}", "ProfileExtraction", "extract-full-name", $"profileUrl={profilePage.Url}");
                return null;
            }

            AppLog.Data($"profileName={fullName}", "ProfileExtraction", "use-discovered-name", $"profileUrl={profilePage.Url};profileName={fullName}");
            await _humanDelayService.DelayAsync(DelayProfile.Profile, "pause between profile field extraction steps", cancellationToken);

            var headline = await ExtractAboutDescriptionAsync(profilePage, cancellationToken);
            if (string.IsNullOrWhiteSpace(headline))
            {
                AppLog.Warn("field not found: AboutDescription", "ProfileExtraction", "extract-role-description", $"profileUrl={profilePage.Url}");
            }
            else
            {
                AppLog.Data($"roleDescription={headline}", "ProfileExtraction", "extract-role-description", $"profileUrl={profilePage.Url};roleDescription={headline}");
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

    private static async Task ResolveProfileExperienceAsync(IPage page, CancellationToken cancellationToken)
    {
        AppLog.Step("waiting for profile experience readiness", "ProfileExtraction", "wait-for-profile-ready", $"profileUrl={page.Url}");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await page.WaitForSelectorAsync(LinkedInSelectors.MainContentSelector, new PageWaitForSelectorOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 8000
        });
        await WaitForAnyVisibleAsync(page, LinkedInSelectors.ExperienceSectionCandidates, 12000, cancellationToken, "experience-section");
        AppLog.Result("profile experience ready", "ProfileExtraction", "wait-for-profile-ready", $"profileUrl={page.Url}");
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

            var sectionLinesTitle = await FindCurrentRoleFromSectionLinesAsync(section, cancellationToken);
            if (!string.IsNullOrWhiteSpace(sectionLinesTitle))
            {
                return sectionLinesTitle;
            }

            var structuredTitle = await ExtractStructuredCurrentRoleAsync(section, cancellationToken);
            if (!string.IsNullOrWhiteSpace(structuredTitle))
            {
                return structuredTitle;
            }

            foreach (var titleSelector in LinkedInSelectors.CurrentExperienceTitleCandidates)
            {
                var title = await FindFirstMatchingTextAsync(section, titleSelector, LooksLikeExperienceTitle, cancellationToken);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return title;
                }
            }

            var fallback = await FindCurrentRoleFromSectionLinesAsync(section, cancellationToken);
            if (!string.IsNullOrWhiteSpace(fallback))
            {
                return fallback;
            }
        }

        return string.Empty;
    }

    private static async Task<string> ExtractAboutDescriptionAsync(IPage page, CancellationToken cancellationToken)
    {
        foreach (var sectionSelector in LinkedInSelectors.AboutSectionCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var section = page.Locator(sectionSelector).First;
            if (await section.CountAsync() == 0 || !await section.IsVisibleAsync())
            {
                continue;
            }

            foreach (var descriptionSelector in LinkedInSelectors.AboutDescriptionCandidates)
            {
                var description = await FindFirstMatchingTextAsync(section, descriptionSelector, LooksLikeAboutDescription, cancellationToken);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return description;
                }
            }

            var fallback = await FindBestLineAsync(section, LooksLikeAboutDescription, cancellationToken, CleanText);
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

    private static async Task CollectMatchingTextsAsync(
        ILocator scope,
        string selector,
        Func<string, bool> predicate,
        CancellationToken cancellationToken,
        IList<string> results,
        int maxResults)
    {
        var matches = scope.Locator(selector);
        var count = await matches.CountAsync();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= maxResults)
            {
                return;
            }

            var candidate = matches.Nth(index);
            if (!await candidate.IsVisibleAsync())
            {
                continue;
            }

            var text = CleanText(await candidate.InnerTextAsync());
            if (!predicate(text) || results.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(text);
        }
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

    private static async Task<string> ExtractStructuredCurrentRoleAsync(ILocator section, CancellationToken cancellationToken)
    {
        var firstVisibleFallback = string.Empty;

        foreach (var itemSelector in LinkedInSelectors.CurrentExperienceItemCandidates)
        {
            var items = section.Locator(itemSelector);
            var count = await items.CountAsync();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items.Nth(index);
                if (!await item.IsVisibleAsync())
                {
                    continue;
                }

                var itemText = CleanText(await item.InnerTextAsync());
                if (string.IsNullOrWhiteSpace(itemText) ||
                    itemText.Equals("Experience", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var title = FindCurrentRoleFromLines(itemText);
                if (string.IsNullOrWhiteSpace(title))
                {
                    title = await ExtractTitleFromExperienceItemAsync(item, cancellationToken);
                }

                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(firstVisibleFallback))
                {
                    firstVisibleFallback = title;
                }

                if (LooksLikeCurrentExperienceItem(itemText))
                {
                    return title;
                }
            }
        }

        return firstVisibleFallback;
    }

    private static async Task<string> ExtractTitleFromExperienceItemAsync(ILocator item, CancellationToken cancellationToken)
    {
        foreach (var titleSelector in LinkedInSelectors.CurrentExperienceTitleCandidates)
        {
            var title = await FindFirstMatchingTextAsync(item, titleSelector, LooksLikeExperienceTitle, cancellationToken);
            if (!string.IsNullOrWhiteSpace(title))
            {
                return title;
            }
        }

        return string.Empty;
    }

    private static async Task<string> FindCurrentRoleFromSectionLinesAsync(ILocator section, CancellationToken cancellationToken)
    {
        var raw = await section.InnerTextAsync();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return FindCurrentRoleFromLines(raw);
    }

    private static string FindCurrentRoleFromLines(string raw)
    {
        var lines = raw
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(CleanText)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!LooksLikeExperienceDateLine(lines[index]))
            {
                continue;
            }

            for (var candidateIndex = index - 1; candidateIndex >= Math.Max(0, index - 3); candidateIndex--)
            {
                var candidate = lines[candidateIndex];
                if (LooksLikeExperienceTitle(candidate))
                {
                    return candidate;
                }
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

    private static bool LooksLikeExperienceDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("Experience", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Show all experiences", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("followers", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("connections", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contact info", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("full-time", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("part-time", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contract", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Regex.IsMatch(value, @"\b(19|20)\d{2}\b"))
        {
            return false;
        }

        return value.Length >= 20 && Regex.IsMatch(value, @"\p{L}");
    }

    private static bool LooksLikeAboutDescription(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("About", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("... more", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("more", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("followers", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("connections", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contact info", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return value.Length >= 30 && Regex.IsMatch(value, @"\p{L}");
    }

    private static bool LooksLikeExperienceTitle(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Equals("Experience", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("About", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Show all experiences", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Remote", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(" at ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (value.Contains("yr", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("mos", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("full-time", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("part-time", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("contract", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Germany", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Berlin", StringComparison.OrdinalIgnoreCase) ||
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

    private static bool LooksLikeCurrentExperienceItem(string value)
    {
        return value.Contains("Present", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("Current", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeExperienceDateLine(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return Regex.IsMatch(
            value,
            @"\b(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{4}\b.*\b(?:Present|\d{4})\b",
            RegexOptions.IgnoreCase);
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
