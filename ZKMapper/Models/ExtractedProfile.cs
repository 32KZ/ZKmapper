namespace ZKMapper.Models;

internal sealed record ExtractedProfile(
    string FullName,
    string Headline,
    string ProfileUrl,
    string CurrentJobTitles,
    DateTime TimestampUtc,
    string SearchQuery);
