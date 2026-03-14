namespace ZKMapper.Infrastructure;

internal sealed record RuntimeOptions(
    bool VerboseEnabled,
    string LogFilePath,
    string[] OriginalArgs);
