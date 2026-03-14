namespace ZKMapper.Infrastructure;

internal static class AppPaths
{
    public static string RootDirectory => Directory.GetCurrentDirectory();

    public static string OutputDirectory => Path.Combine(RootDirectory, "output");

    public static string SessionDirectory => Path.Combine(RootDirectory, "session");

    public static string SessionStatePath => Path.Combine(SessionDirectory, "linkedin_session.json");

    public static string LogDirectory => Path.Combine(RootDirectory, "logs");

    public static string DebugDirectory => Path.Combine(LogDirectory, "debug");

    public static string DebugPeoplePageScreenshotPath => Path.Combine(DebugDirectory, "debug_people_page.png");

    public static string ConfigDirectory => Path.Combine(RootDirectory, "config");

    public static string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");

    public static string InputDirectory => Path.Combine(RootDirectory, "input");

    public static string DefaultBatchInputFilePath => Path.Combine(InputDirectory, "Company.txt");

    public static string CreateRunLogPath(DateTime timestamp)
    {
        return Path.Combine(LogDirectory, $"run_{timestamp:yyyy_MM_dd_HH_mm_ss}.log");
    }
}
