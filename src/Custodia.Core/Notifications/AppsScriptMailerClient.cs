using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TundraDrive.Core.Configuration;

namespace TundraDrive.Core.Notifications;

public sealed class AppsScriptMailerClient : ITundraMailerClient, IDisposable
{
    public const string OfficialSenderEmail = "contato.tundralab@gmail.com";
    public const string DefaultWebhookUrl = "";

    private readonly IAppSettingsService _settingsService;
    private readonly ILogger<AppsScriptMailerClient> _logger;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public AppsScriptMailerClient(
        IAppSettingsService settingsService,
        ILogger<AppsScriptMailerClient> logger,
        HttpMessageHandler? httpHandler = null)
    {
        _settingsService = settingsService;
        _logger = logger;
        _ownsHttpClient = httpHandler is null;
        _httpClient = httpHandler is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(10) }
            : new HttpClient(httpHandler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
    }

    public string? ResolveWebhookUrl()
    {
        var envUrl = Environment.GetEnvironmentVariable("CUSTODIA_MAILER_WEBHOOK_URL");
        if (Uri.TryCreate(envUrl, UriKind.Absolute, out _))
        {
            return envUrl.Trim();
        }

        var settingsUrl = _settingsService.Current.MailerWebhookUrl;
        if (Uri.TryCreate(settingsUrl, UriKind.Absolute, out _))
        {
            return settingsUrl.Trim();
        }

        return Uri.TryCreate(DefaultWebhookUrl, UriKind.Absolute, out _)
            ? DefaultWebhookUrl.Trim()
            : null;
    }

    public async Task<bool> TrySendLifecycleEmailAsync(
        string recipientEmail,
        string? recipientName,
        bool isConnected,
        string language = "pt-BR",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            return false;
        }

        var webhookUrl = ResolveWebhookUrl();
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogDebug("TundraLab Apps Script webhook URL is not configured; lifecycle email skipped.");
            return false;
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var payloadJson = BuildPayloadJson(recipientEmail, recipientName, isConnected, language);
            using var req = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
            {
                Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
            };

            using var resp = await _httpClient.SendAsync(req, cts.Token);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(cts.Token);
                _logger.LogWarning("TundraLab mailer webhook returned HTTP {StatusCode}: {Body}", (int)resp.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not deliver lifecycle email request to TundraLab mailer webhook.");
            return false;
        }
    }

    internal static string BuildPayloadJson(
        string recipientEmail,
        string? recipientName,
        bool isConnected,
        string language)
    {
        var payload = new
        {
            source = "custodia-desktop",
            eventType = isConnected ? "connect" : "revoke",
            senderEmail = OfficialSenderEmail,
            recipientEmail = recipientEmail.Trim(),
            recipientName = string.IsNullOrWhiteSpace(recipientName) ? recipientEmail.Trim() : recipientName.Trim(),
            appName = "Custódia",
            appVersion = AppVersionInfo.CurrentVersion,
            language = string.IsNullOrWhiteSpace(language) ? "pt-BR" : language.Trim(),
            timestampUtc = DateTimeOffset.UtcNow.ToString("O")
        };

        return JsonSerializer.Serialize(payload);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
