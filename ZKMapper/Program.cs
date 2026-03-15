using System.Diagnostics;
using Serilog;
using ZKMapper.Infrastructure;
using ZKMapper.Models;
using ZKMapper.Services;

namespace ZKMapper;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        var runStartedAt = DateTime.Now;
        var runtimeOptions = ParseRuntimeOptions(args, runStartedAt);
        var statistics = new RunStatistics();
        var totalRuntime = Stopwatch.StartNew();

        Directory.CreateDirectory(AppPaths.OutputDirectory);
        Directory.CreateDirectory(AppPaths.SessionDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);
        Directory.CreateDirectory(AppPaths.DebugDirectory);
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        Directory.CreateDirectory(AppPaths.InputDirectory);
        Directory.CreateDirectory(AppPaths.WebhookDirectory);
        Directory.CreateDirectory(AppPaths.WebhookAuthenticationDirectory);

        LoggerConsoleHost.Start(runtimeOptions.LogFilePath, runtimeOptions.RunId);

        AppLog.TraceEnabled = runtimeOptions.VerboseEnabled;
        Log.Logger = LoggingSetup.CreateLogger(runtimeOptions);

        try
        {
            using var runTimer = ExecutionTimer.Start("ApplicationRun");
            LogStartup(runtimeOptions);

            var contextFactory = new PlaywrightContextFactory();
            var configurationService = new ConfigurationService();
            var promptService = new ConsolePromptService();
            var consoleUiService = new ConsoleUiService();
            consoleUiService.ConfigureRunContext(runtimeOptions.RunId, runtimeOptions.LogFilePath);
            var webhookAuthenticationStorage = new WebhookAuthenticationStorageService();
            var webhookService = new WebhookService(configurationService, webhookAuthenticationStorage);
            var humanDelayService = new HumanDelayService(configurationService);
            var retryService = new RetryService();
            var scrollExhaustionService = new ScrollExhaustionService(humanDelayService);

            consoleUiService.ShowStartupBanner();

            var app = new MapperApplication(
                promptService,
                consoleUiService,
                new SessionStateManager(),
                new BrowserManager(contextFactory),
                new LinkedInRegionMapper(),
                new LinkedInPeopleSearchUrlBuilder(),
                new LinkedInQueryService(retryService, scrollExhaustionService, humanDelayService),
                new ProfileExtractionService(retryService, humanDelayService),
                new EmailGenerationService(),
                humanDelayService,
                statistics);
            var menuService = new MenuService(
                promptService,
                consoleUiService,
                app,
                configurationService,
                new InputFileLoader(),
                webhookAuthenticationStorage,
                webhookService);

            var command = runtimeOptions.OriginalArgs
                .FirstOrDefault(arg => !arg.StartsWith("--", StringComparison.Ordinal))
                ?.Trim()
                .ToLowerInvariant();

            AppLog.Next(
                command == "auth" ? "executing auth workflow" : "executing collection workflow",
                "ProgramStart",
                "dispatch-command",
                $"command={command ?? "collect"}");

            return command switch
            {
                "auth" => await app.RunAuthSetupAsync(),
                _ => await menuService.ShowMainMenuAsync()
            };
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "Unhandled application error", "ProgramStart", "fatal", $"args={string.Join(' ', runtimeOptions.OriginalArgs)}");
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            totalRuntime.Stop();
            AppLog.Summary(
                "run complete",
                $"profilesScanned={statistics.ProfilesScanned};recordsWritten={statistics.RecordsWritten};queriesExecuted={statistics.QueriesExecuted}",
                AppLog.FormatDuration(totalRuntime.Elapsed));
            AppLog.Info("ZKMapper run ended", "ProgramEnd", "shutdown", $"logFile={runtimeOptions.LogFilePath}");
            await Log.CloseAndFlushAsync();
        }
    }

    private static RuntimeOptions ParseRuntimeOptions(string[] args, DateTime runStartedAt)
    {
        var verboseEnabled = true;
        var runId = AppPaths.CreateRunId(runStartedAt);

        foreach (var arg in args)
        {
            if (string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase))
            {
                verboseEnabled = true;
            }

            if (string.Equals(arg, "--no-verbose", StringComparison.OrdinalIgnoreCase))
            {
                verboseEnabled = false;
            }
        }

        return new RuntimeOptions(verboseEnabled, runId, AppPaths.CreateRunLogPath(runId), args);
    }

    private static void LogStartup(RuntimeOptions runtimeOptions)
    {
        AppLog.Step("Application start", "ProgramStart", "initialize", $"logFile={runtimeOptions.LogFilePath}");
        AppLog.Data($"runId={runtimeOptions.RunId}", "ProgramStart", "run-id", $"runId={runtimeOptions.RunId}");
        AppLog.Data($".NET runtime={Environment.Version}", "ProgramStart", "runtime-version", $"runtime={Environment.Version}");
        AppLog.Data($"workingDirectory={AppPaths.RootDirectory}", "ProgramStart", "working-directory", $"cwd={AppPaths.RootDirectory}");
        AppLog.Data($"cliArgs={string.Join(' ', runtimeOptions.OriginalArgs)}", "ProgramStart", "cli-arguments", $"args={string.Join(' ', runtimeOptions.OriginalArgs)}");
        AppLog.Result(
            runtimeOptions.VerboseEnabled ? "TRACE logging enabled" : "Verbose logging disabled",
            "ProgramStart",
            "configure-logging",
            $"verbose={runtimeOptions.VerboseEnabled}");

        var gitCommit = TryGetGitCommitHash();
        AppLog.Data($"gitCommit={gitCommit ?? "unavailable"}", "ProgramStart", "git-commit", $"gitCommit={gitCommit ?? "unavailable"}");
    }

    private static string? TryGetGitCommitHash()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = $"-c safe.directory={AppPaths.RootDirectory.Replace("\\", "/", StringComparison.Ordinal)} rev-parse HEAD",
                    WorkingDirectory = AppPaths.RootDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(3000);

            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output)
                ? output
                : null;
        }
        catch
        {
            return null;
        }
    }
}
