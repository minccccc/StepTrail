namespace StepTrail.Shared.Definitions;

public sealed class WebhookTriggerConfiguration
{
    private WebhookTriggerConfiguration()
    {
        RouteKey = string.Empty;
        HttpMethod = "POST";
    }

    public WebhookTriggerConfiguration(string routeKey, string httpMethod = "POST")
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            throw new ArgumentException("Webhook route key must not be empty.", nameof(routeKey));
        if (string.IsNullOrWhiteSpace(httpMethod))
            throw new ArgumentException("Webhook HTTP method must not be empty.", nameof(httpMethod));

        RouteKey = routeKey.Trim();
        HttpMethod = httpMethod.Trim().ToUpperInvariant();
    }

    public string RouteKey { get; private set; }
    public string HttpMethod { get; private set; }
}
