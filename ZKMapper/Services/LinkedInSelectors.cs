namespace ZKMapper.Services;

internal static class LinkedInSelectors
{
    public const string ExperienceItemSelector = "li, div";
    public const string MainContentSelector = "main";
    public const string BodySelector = "body";

    public static readonly string[] ResultsContainerCandidates =
    {
        "main[role='main']",
        "main",
        "[aria-label*='Search results']",
        "ul[role='list']"
    };

    public static readonly string[] HeroCardContainerCandidates =
    {
        "section:has(div.org-people-profile-card)",
        "section:has(li.org-people-profiles-module__profile-item)",
        "main",
        "ul[role='list']"
    };

    public static readonly string[] HeroCardCandidates =
    {
        "div.org-people-profile-card",
        "li.org-people-profiles-module__profile-item",
        "li:has(a[href*='/in/'])"
    };

    public static readonly string[] HeroCardNameCandidates =
    {
        "a[href*='/in/'] span[aria-hidden='true']",
        "span[aria-hidden='true']"
    };

    public static readonly string[] HeroCardLinkCandidates =
    {
        "a[href*='/in/']"
    };

    public static readonly string[] ProfileLinkCandidates =
    {
        "a[data-test-app-aware-link][href*='/in/']",
        "a[aria-label][href*='/in/']",
        "a[href*='/in/']"
    };

    public static readonly string[] ShowMoreButtonCandidates =
    {
        "button[aria-label*='Show more']",
        "button[data-control-name*='show_more']",
        "button:has-text('Show more results')",
        "button:has-text('Show more')",
        "button:has-text('See more results')"
    };

    public static readonly string[] ProfileHeaderNameCandidates =
    {
        "main h1",
        "h1",
        "div[class*='top-card'] h1",
        "div[class*='pv-text-details'] h1",
        "main h1 span[aria-hidden='true']",
        "main section h1"
    };

    public static readonly string[] ProfileShellCandidates =
    {
        "main[role='main']",
        "main",
        "section[class*='top-card']",
        "div.pv-text-details__left-panel",
        "div.ph5"
    };

    public static readonly string[] ProfileTopCardCandidates =
    {
        "section[class*='top-card']",
        "div.pv-text-details__left-panel",
        "div.ph5",
        "main section.artdeco-card",
        "main div[class*='profile-top-card']"
    };

    public static readonly string[] ProfileHeaderReadinessCandidates =
    {
        "main h1",
        "div[class*='pv-text-details'] h1",
        "div[class*='top-card'] h1",
        "div[class*='pv-text-details']",
        "main div.text-body-medium",
        "main div[class*='profile-top-card']"
    };

    public static readonly string[] ProfileHeadlineCandidates =
    {
        "div.text-body-medium",
        "div[class*='pv-text-details'] div.text-body-medium",
        "main div.text-body-medium",
        "main div.text-body-medium.break-words",
        "main div.text-body-medium",
        "main .text-body-medium",
        "main .text-body-medium.break-words"
    };

    public static readonly string[] ExperienceSectionCandidates =
    {
        "section#experience",
        "section[aria-label*='Experience']",
        "section:has-text('Experience')",
        "main section[id*='experience']",
        "main div:has(> #experience)"
    };

    public static readonly string[] CurrentExperienceTitleCandidates =
    {
        "h3",
        "span[aria-hidden='true']",
        "li div.t-bold span[aria-hidden='true']",
        "li span[aria-hidden='true']",
        "div[data-view-name='profile-component-entity'] div.t-bold span[aria-hidden='true']",
        "div[data-view-name='profile-component-entity'] span[aria-hidden='true']",
        "div.t-bold span[aria-hidden='true']"
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
