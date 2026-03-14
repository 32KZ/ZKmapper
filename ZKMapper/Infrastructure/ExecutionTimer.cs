using System.Diagnostics;

namespace ZKMapper.Infrastructure;

internal sealed class ExecutionTimer : IDisposable
{
    private readonly Stopwatch _stopwatch;
    private readonly string _name;
    private bool _disposed;

    private ExecutionTimer(string name)
    {
        _name = name;
        _stopwatch = Stopwatch.StartNew();
    }

    public static ExecutionTimer Start(string name)
    {
        return new ExecutionTimer(name);
    }

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _stopwatch.Stop();
        AppLog.Timer(_name, _stopwatch.Elapsed);
        _disposed = true;
    }
}
