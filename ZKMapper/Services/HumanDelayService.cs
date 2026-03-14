using ZKMapper.Models;
using ZKMapper.Infrastructure;

namespace ZKMapper.Services;

internal sealed class HumanDelayService
{
    private readonly ConfigurationService _configurationService;

    public HumanDelayService(ConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public Task DelayAsync(DelayProfile profile, string reason, CancellationToken cancellationToken = default)
    {
        var range = _configurationService.GetDelayRange(profile);
        return DelayAsync(range, reason, cancellationToken);
    }

    public Task DelayAsync(DelayRange range, string reason, CancellationToken cancellationToken = default)
    {
        range.Validate();
        var delay = range.MinMs == range.MaxMs
            ? range.MinMs
            : Random.Shared.Next(range.MinMs, range.MaxMs + 1);

        AppLog.Wait(
            $"sleeping {TimeSpan.FromMilliseconds(delay).TotalSeconds:F1} seconds",
            "HumanDelay",
            "delay",
            $"reason={reason};delayMs={delay};minMs={range.MinMs};maxMs={range.MaxMs}");

        return Task.Delay(delay, cancellationToken);
    }
}
