namespace ZKMapper.Models;

internal sealed record RunMetadata(
    int RunNumber,
    DateTime StartedUtc,
    string TimestampToken);
