namespace StepTrail.Api.Services;

/// <summary>
/// Builds the public webhook path from a persisted webhook route key.
/// The route key is the stable endpoint identity; the generated path is the
/// address external systems will call once intake is wired up.
/// </summary>
public static class WebhookEndpointAddressGenerator
{
    public static string BuildRelativePath(string routeKey)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            throw new ArgumentException("Webhook route key must not be empty.", nameof(routeKey));

        return $"/webhooks/{Uri.EscapeDataString(routeKey.Trim())}";
    }
}
