namespace ZKMapper.Models;

internal sealed class MappedContactRow
{
    public string CompanyName { get; init; } = string.Empty;
    public string SearchCountry { get; init; } = string.Empty;
    public string SearchQuery { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string Headline { get; init; } = string.Empty;
    public string CurrentJobTitles { get; init; } = string.Empty;
    public string ProfileURL { get; init; } = string.Empty;
    public string EmailPrimary { get; init; } = string.Empty;
    public string EmailAlt1 { get; init; } = string.Empty;
    public string EmailAlt2 { get; init; } = string.Empty;
    public string EmailAlt3 { get; init; } = string.Empty;
    public string TimestampUTC { get; init; } = string.Empty;
}
