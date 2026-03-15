namespace ZKMapper.Models;

internal sealed class AppSettings
{
    public DelaySettings Delays { get; set; } = new();
    public WebhookSettings Webhook { get; set; } = new();
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

internal sealed class WebhookSettings
{
    public WebhookMode ActiveMode { get; set; } = WebhookMode.Test;
    public string ProductionWebhookUrl { get; set; } = string.Empty;
    public string TestWebhookUrl { get; set; } = string.Empty;
    public bool HeaderAuthenticationEnabled { get; set; }
}

internal enum WebhookMode
{
    Production,
    Test
}

internal sealed record WebhookAuthenticationValues(string HeaderName, string HeaderSecret)
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(HeaderName) &&
        !string.IsNullOrWhiteSpace(HeaderSecret);
}

internal sealed record WebhookSendResult(
    bool Success,
    int? StatusCode,
    string ResponseBody,
    string ErrorMessage);
