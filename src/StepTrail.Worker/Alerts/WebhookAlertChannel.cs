using System.Net.Http.Json;

namespace StepTrail.Worker.Alerts;

/// <summary>
/// POSTs the AlertPayload as JSON to a configured URL.
/// Registered only when Alerts:WebhookUrl is set in configuration.
/// Suitable for Slack incoming webhooks, Teams connectors, or any custom HTTP receiver.
/// </summary>
public sealed class WebhookAlertChannel : IAlertChannel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _webhookUrl;
    private readonly ILogger<WebhookAlertChannel> _logger;

    public WebhookAlertChannel(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<WebhookAlertChannel> logger)
    {
        _httpClientFactory = httpClientFactory;
        _webhookUrl = configuration.GetValue<string>("Alerts:WebhookUrl")
            ?? throw new InvalidOperationException("Alerts:WebhookUrl must be set when WebhookAlertChannel is registered.");
        _logger = logger;
    }

    public async Task SendAsync(AlertPayload payload, CancellationToken ct)
    {
        var httpClient = _httpClientFactory.CreateClient("AlertWebhook");

        var response = await httpClient.PostAsJsonAsync(_webhookUrl, payload, ct);

        if (!response.IsSuccessStatusCode)
            _logger.LogWarning(
                "Webhook alert delivery failed — {StatusCode} from {Url}",
                (int)response.StatusCode, _webhookUrl);
        else
            _logger.LogDebug(
                "Webhook alert [{AlertType}] delivered to {Url}",
                payload.AlertType, _webhookUrl);
    }
}
