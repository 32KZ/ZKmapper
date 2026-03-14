using Serilog;
using Serilog.Events;

namespace ZKMapper.Infrastructure;

internal static class AppLog
{
    public static bool TraceEnabled { get; set; } = true;

    public static void Trace(string message, string step = "", string action = "", string data = "", string duration = "")
    {
        if (!TraceEnabled)
        {
            return;
        }

        Write(LogEventLevel.Verbose, "[TRACE]", message, step, action, data, duration);
    }

    public static void Debug(string message, string step = "", string action = "", string data = "", string duration = "")
    {
        Write(LogEventLevel.Debug, "[DEBUG]", message, step, action, data, duration);
    }

    public static void Info(string message, string step = "", string action = "", string data = "", string duration = "")
    {
        Write(LogEventLevel.Information, "[INFO]", message, step, action, data, duration);
    }

    public static void Warn(string message, string step = "", string action = "", string data = "", string duration = "")
    {
        Write(LogEventLevel.Warning, "[WARN]", message, step, action, data, duration);
    }

    public static void Warn(Exception ex, string message, string step = "", string action = "", string data = "", string duration = "")
    {
        Context(step, action, data, duration).Warning(ex, "{Prefix} {Message}", "[WARN]", message);
    }

    public static void Error(Exception ex, string message, string step = "", string action = "", string data = "", string duration = "")
    {
        Context(step, action, data, duration).Error(ex, "{Prefix} {Message}", "[ERROR]", message);
    }

    public static void Step(string message, string step, string action = "", string data = "")
    {
        Write(LogEventLevel.Information, "[STEP]", message, step, action, data, string.Empty);
    }

    public static void Data(string message, string step, string action = "", string data = "")
    {
        Write(LogEventLevel.Information, "[DATA]", message, step, action, data, string.Empty);
    }

    public static void Action(string message, string step, string action, string data = "")
    {
        Write(LogEventLevel.Information, "[ACTION]", message, step, action, data, string.Empty);
    }

    public static void Wait(string message, string step, string action = "", string data = "")
    {
        Write(LogEventLevel.Information, "[WAIT]", message, step, action, data, string.Empty);
    }

    public static void Result(string message, string step, string action = "", string data = "", string duration = "")
    {
        Write(LogEventLevel.Information, "[RESULT]", message, step, action, data, duration);
    }

    public static void Next(string message, string step, string action = "", string data = "")
    {
        Write(LogEventLevel.Information, "[NEXT]", message, step, action, data, string.Empty);
    }

    public static void Input(string message, string data = "")
    {
        Write(LogEventLevel.Information, "[INPUT]", message, "InputCapture", "capture-input", data, string.Empty);
    }

    public static void Session(string message, string action = "", string data = "")
    {
        Write(LogEventLevel.Information, "[SESSION]", message, "SessionHandling", action, data, string.Empty);
    }

    public static void Timer(string name, TimeSpan elapsed)
    {
        Write(LogEventLevel.Information, "[TIMER]", $"{name} = {FormatDuration(elapsed)}", name, "measure", string.Empty, FormatDuration(elapsed));
    }

    public static void Summary(string message, string data = "", string duration = "")
    {
        Write(LogEventLevel.Information, "[SUMMARY]", message, "RunSummary", "complete", data, duration);
    }

    private static void Write(LogEventLevel level, string prefix, string message, string step, string action, string data, string duration)
    {
        Context(step, action, data, duration).Write(level, "{Prefix} {Message}", prefix, message);
    }

    private static ILogger Context(string step, string action, string data, string duration)
    {
        return Log.ForContext("step", step ?? string.Empty)
            .ForContext("action", action ?? string.Empty)
            .ForContext("data", data ?? string.Empty)
            .ForContext("duration", duration ?? string.Empty);
    }

    public static string FormatDuration(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds >= 1)
        {
            return $"{elapsed.TotalSeconds:F1}s";
        }

        return $"{elapsed.TotalMilliseconds:F0}ms";
    }
}
