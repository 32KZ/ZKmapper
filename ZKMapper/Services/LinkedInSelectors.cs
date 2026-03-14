namespace ZKMapper.Services;

internal static class LinkedInSelectors
{
    public const string ExperienceItemSelector = "li, div";

    public static readonly string[] PeopleTabCandidates =
    {
        "[data-control-name='page_member_main_nav_people_tab']",
        "a[aria-label='People']",
        "a[aria-current='page'][href*='/people/']",
        "a[href*='/people/']"
    };

    public static readonly string[] PeopleSearchInputCandidates =
    {
        "main input[placeholder*='Search employees by title']",
        "main input[placeholder*='Search employees']",
        "main input[placeholder*='keyword or school']",
        "main input[placeholder*='Search']"
    };

    public static readonly string[] ResultsContainerCandidates =
    {
        "main[role='main']",
        "main",
        "[aria-label*='Search results']",
        "ul[role='list']"
    };

    public static readonly string[] ShowMoreButtonCandidates =
    {
        "button[aria-label*='Show more']",
        "button[data-control-name*='show_more']",
        "button:has-text('Show more results')",
        "button:has-text('Show more')"
    };

    public static readonly string[] ProfileLinkCandidates =
    {
        "a[data-test-app-aware-link][href*='/in/']",
        "a[aria-label][href*='/in/']",
        "a[href*='/in/']"
    };

    public static readonly string[] ProfileHeaderNameCandidates =
    {
        "h1",
        "main h1[dir='ltr']",
        "section h1"
    };

    public static readonly string[] ExperienceSectionCandidates =
    {
        "section:has(#experience)",
        "section[aria-label*='Experience']",
        "section:has-text('Experience')"
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
