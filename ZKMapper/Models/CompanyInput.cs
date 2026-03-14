namespace ZKMapper.Models;

internal sealed record CompanyInput(
    string CompanyName,
    string CompanyDomain,
    string CompanyLinkedInUrl,
    string SearchCountry,
    IReadOnlyList<string> TitleFilters);
