namespace ZKMapper.Models;

internal sealed class AppSettings
{
    public DelaySettings Delays { get; set; } = new();
}

internal sealed class DelaySettings
{
    public int NavigationMinMs { get; set; } = 3000;
    public int NavigationMaxMs { get; set; } = 7000;
    public int SearchMinMs { get; set; } = 6000;
    public int SearchMaxMs { get; set; } = 12000;
    public int ProfileOpenMinMs { get; set; } = 15000;
    public int ProfileOpenMaxMs { get; set; } = 35000;
}
