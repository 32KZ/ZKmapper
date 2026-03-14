namespace ZKMapper.Models;

internal sealed record ExtractedProfile(
    string FullName,
    string ProfileUrl,
    string CurrentJobTitles,
    DateTime TimestampUtc,
    string SearchQuery);
