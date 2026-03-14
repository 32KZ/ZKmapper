using Serilog;
using ZKMapper.Infrastructure;
using ZKMapper.Services;

namespace ZKMapper;

internal sealed class Program
{
    public static async Task<int> Main(string[] args)
    {
        Directory.CreateDirectory(AppPaths.OutputDirectory);
        Directory.CreateDirectory(AppPaths.SessionDirectory);
        Directory.CreateDirectory(AppPaths.LogDirectory);

        Log.Logger = LoggingSetup.CreateLogger();

        try
        {
            Log.Information("Starting ZKMapper run");

            var app = new MapperApplication(
                new ConsolePromptService(),
                new SessionStateManager(),
                new BrowserManager(),
                new LinkedInNavigationService(new RetryService()),
                new LinkedInQueryService(new RetryService()),
                new ProfileExtractionService(new RetryService()),
                new EmailGenerationService());

            var command = args.FirstOrDefault()?.Trim().ToLowerInvariant();
            return command switch
            {
                "auth" => await app.RunAuthSetupAsync(),
                _ => await app.RunCollectionAsync()
            };
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Unhandled application error");
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            Log.Information("ZKMapper run ended");
            await Log.CloseAndFlushAsync();
        }
    }
}
