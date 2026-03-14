namespace ZKMapper.Services;

internal static class LinkedInSelectors
{
    public const string ExperienceItemSelector = "li, div";

    public static readonly string[] PeopleTabCandidates =
    {
        "a[href*='/people/']",
        "a:has-text('People')",
        "[data-control-name='page_member_main_nav_people_tab']"
    };

    public static readonly string[] PeopleSearchInputCandidates =
    {
        "input[placeholder*='Search by title']",
        "input[placeholder*='Search employees']",
        "input[placeholder*='Search']",
        "input[role='combobox']"
    };

    public static readonly string[] ResultsContainerCandidates =
    {
        "main",
        ".org-people-module",
        ".scaffold-finite-scroll",
        "div.org-people-profile-card__card-spacing",
        "ul"
    };

    public static readonly string[] ShowMoreButtonCandidates =
    {
        "button:has-text('Show more results')",
        "button:has-text('Show more')",
        "button[aria-label*='Show more']"
    };

    public static readonly string[] ProfileLinkCandidates =
    {
        "a[href*='/in/']",
        "a[data-test-app-aware-link][href*='/in/']"
    };

    public static readonly string[] ProfileHeaderNameCandidates =
    {
        "h1",
        ".text-heading-xlarge",
        ".pv-text-details__left-panel h1"
    };

    public static readonly string[] ExperienceSectionCandidates =
    {
        "section:has(#experience)",
        "section:has-text('Experience')",
        "main section"
    };

    public static string BuildProfileLinkSelector(string href)
    {
        var relativeHref = TryGetRelativeLinkedInPath(href);
        var selectors = new List<string>();

        if (!string.IsNullOrWhiteSpace(href))
        {
            selectors.Add($"a[href='{EscapeCssAttribute(href)}']");
        }

        if (!string.IsNullOrWhiteSpace(relativeHref))
        {
            selectors.Add($"a[href='{EscapeCssAttribute(relativeHref)}']");
        }

        return string.Join(", ", selectors.Distinct(StringComparer.Ordinal));
    }

    private static string TryGetRelativeLinkedInPath(string href)
    {
        return Uri.TryCreate(href, UriKind.Absolute, out var uri)
            ? uri.PathAndQuery
            : href;
    }

    private static string EscapeCssAttribute(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
    }
}
