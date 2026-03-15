namespace ZKMapper.Infrastructure;

internal sealed record RuntimeOptions(
    bool VerboseEnabled,
    string RunId,
    string LogFilePath,
    string[] OriginalArgs);
