namespace ZKMapper.Models;

internal sealed class AppSettings
{
    public DelaySettings Delays { get; set; } = new();
}

internal sealed class DelaySettings
{
    public int NavigationMinMs { get; set; } = 3000;
    public int NavigationMaxMs { get; set; } = 7000;
    public int ScrollMinMs { get; set; } = 2000;
    public int ScrollMaxMs { get; set; } = 6000;
    public int ProfileMinMs { get; set; } = 10000;
    public int ProfileMaxMs { get; set; } = 25000;
}
