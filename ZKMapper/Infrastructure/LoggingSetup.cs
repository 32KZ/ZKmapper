using Serilog;
using Serilog.Events;

namespace ZKMapper.Infrastructure;

internal static class LoggingSetup
{
    public static ILogger CreateLogger(RuntimeOptions runtimeOptions)
    {
        var minimumLevel = runtimeOptions.VerboseEnabled
            ? LogEventLevel.Verbose
            : LogEventLevel.Information;

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                runtimeOptions.LogFilePath,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} step={step} action={action} data={data} duration={duration} Company={Company} Query={Query} ProfileUrl={ProfileUrl}{NewLine}{Exception}")
            .CreateLogger();
    }
}
