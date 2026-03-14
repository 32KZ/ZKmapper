namespace ZKMapper.Services;

internal static class LinkedInSelectors
{
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
}
