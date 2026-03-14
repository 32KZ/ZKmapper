namespace ZKMapper.Models;

internal sealed record DelayRange(int MinMs, int MaxMs)
{
    public void Validate()
    {
        if (MinMs < 0 || MaxMs < 0)
        {
            throw new InvalidOperationException("Delay values must be non-negative.");
        }

        if (MaxMs < MinMs)
        {
            throw new InvalidOperationException("Maximum delay must be greater than or equal to minimum delay.");
        }
    }
}
