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
            Log.Information("Application start");

            var contextFactory = new PlaywrightContextFactory();
            var humanDelayService = new HumanDelayService();
            var retryService = new RetryService();
            var scrollExhaustionService = new ScrollExhaustionService(humanDelayService);

            var app = new MapperApplication(
                new ConsolePromptService(),
                new SessionStateManager(),
                new BrowserManager(contextFactory),
                new LinkedInNavigationService(retryService, humanDelayService),
                new LinkedInQueryService(retryService, scrollExhaustionService, humanDelayService),
                new ProfileExtractionService(retryService, humanDelayService),
                new EmailGenerationService(),
                humanDelayService);

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
