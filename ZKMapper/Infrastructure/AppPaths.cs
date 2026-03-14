namespace ZKMapper.Infrastructure;

internal static class AppPaths
{
    public static string RootDirectory => Directory.GetCurrentDirectory();

    public static string OutputDirectory => Path.Combine(RootDirectory, "output");

    public static string SessionDirectory => Path.Combine(RootDirectory, "session");

    public static string SessionStatePath => Path.Combine(SessionDirectory, "linkedin_session.json");

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");
}
