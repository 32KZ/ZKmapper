using Serilog;
using Serilog.Events;

namespace ZKMapper.Infrastructure;

internal static class LoggingSetup
{
    public static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "zkmapper-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} Company={Company} Query={Query} ProfileUrl={ProfileUrl}{NewLine}{Exception}")
            .CreateLogger();
    }
}
