using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ZKMapper.Infrastructure;
using ZKMapper.Models;

namespace ZKMapper.Services;

internal sealed class WebhookService
{
    private static readonly HttpClient HttpClient = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConfigurationService _configurationService;
    private readonly WebhookAuthenticationStorageService _authenticationStorage;

    public WebhookService(
        ConfigurationService configurationService,
        WebhookAuthenticationStorageService authenticationStorage)
    {
        _configurationService = configurationService;
        _authenticationStorage = authenticationStorage;
    }

    public string GetActiveWebhookUrl()
    {
        var settings = _configurationService.GetWebhookSettings();
        return settings.ActiveMode == WebhookMode.Production
            ? settings.ProductionWebhookUrl
            : settings.TestWebhookUrl;
    }

    public async Task<WebhookSendResult> SendFilesAsync(string csvPath, string logPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(csvPath))
        {
            return new WebhookSendResult(false, null, string.Empty, $"CSV file not found: {csvPath}");
        }

        if (!File.Exists(logPath))
        {
            return new WebhookSendResult(false, null, string.Empty, $"Log file not found: {logPath}");
        }

        var settings = _configurationService.GetWebhookSettings();
        var webhookUrl = GetActiveWebhookUrl();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            return new WebhookSendResult(false, null, string.Empty, "Webhook URL is not configured.");
        }

        var auth = _authenticationStorage.Load();
        if (settings.HeaderAuthenticationEnabled && !auth.IsConfigured)
        {
            return new WebhookSendResult(false, null, string.Empty, "Header authentication is enabled but header credentials are not configured.");
        }

        var csvInfo = new FileInfo(csvPath);
        var logInfo = new FileInfo(logPath);
        AppLog.Info("[WEBHOOK] sending webhook", "Webhook", "send", $"mode={settings.ActiveMode};url={webhookUrl}");
        AppLog.Data($"csvSizeBytes={csvInfo.Length};logSizeBytes={logInfo.Length}", "Webhook", "file-sizes", $"csvPath={csvPath};logPath={logPath}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl);
            if (settings.HeaderAuthenticationEnabled)
            {
                request.Headers.TryAddWithoutValidation(auth.HeaderName, auth.HeaderSecret);
            }

            var metadata = new
            {
                source = "ZKMapper",
                timestamp = DateTime.UtcNow.ToString("O"),
                version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown",
                runId = ResolveRunId(logInfo),
                csvFileName = csvInfo.Name,
                logFileName = logInfo.Name
            };

            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, "application/json"), "metadata");
            content.Add(CreateFileContent(csvPath), "csv_file", csvInfo.Name);
            content.Add(CreateFileContent(logPath), "log_file", logInfo.Name);
            request.Content = content;

            using var response = await HttpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            AppLog.Info("[WEBHOOK] webhook response received", "Webhook", "response", $"statusCode={(int)response.StatusCode}");

            return new WebhookSendResult(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                NormalizeResponseBody(responseBody),
                response.IsSuccessStatusCode ? string.Empty : $"HTTP {(int)response.StatusCode} {response.StatusCode}");
        }
        catch (Exception ex)
        {
            AppLog.Error(ex, "webhook send failed", "Webhook", "send", $"url={webhookUrl}");
            return new WebhookSendResult(false, null, string.Empty, ex.Message);
        }
    }

    private static StreamContent CreateFileContent(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return content;
    }

    private static string ResolveRunId(FileInfo logInfo)
    {
        return Path.GetFileNameWithoutExtension(logInfo.Name);
    }

    private static string NormalizeResponseBody(string responseBody)
    {
        return string.IsNullOrWhiteSpace(responseBody) ? "<empty>" : responseBody.Trim();
    }
}
