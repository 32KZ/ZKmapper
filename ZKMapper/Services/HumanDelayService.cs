namespace ZKMapper.Services;

internal sealed class HumanDelayService
{
    public Task DelayAsync(int minSeconds, int maxSeconds, CancellationToken cancellationToken = default)
    {
        if (minSeconds < 0 || maxSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minSeconds), "Delay values must be non-negative.");
        }

        if (maxSeconds < minSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSeconds), "Maximum delay must be greater than or equal to minimum delay.");
        }

        var minMilliseconds = minSeconds * 1000;
        var maxMilliseconds = maxSeconds * 1000;
        var delay = minMilliseconds == maxMilliseconds
            ? minMilliseconds
            : Random.Shared.Next(minMilliseconds, maxMilliseconds + 1);

        return Task.Delay(delay, cancellationToken);
    }
}
