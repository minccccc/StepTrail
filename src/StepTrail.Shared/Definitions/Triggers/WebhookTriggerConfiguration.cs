namespace StepTrail.Shared.Definitions;

public sealed class WebhookTriggerConfiguration
{
    private readonly List<WebhookInputMapping> _inputMappings = [];

    private WebhookTriggerConfiguration()
    {
        RouteKey = string.Empty;
        HttpMethod = "POST";
        SignatureValidation = null;
        IdempotencyKeyExtraction = null;
    }

    public WebhookTriggerConfiguration(
        string routeKey,
        string httpMethod = "POST",
        WebhookSignatureValidationConfiguration? signatureValidation = null,
        IEnumerable<WebhookInputMapping>? inputMappings = null,
        WebhookIdempotencyKeyExtractionConfiguration? idempotencyKeyExtraction = null)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            throw new ArgumentException("Webhook route key must not be empty.", nameof(routeKey));
        if (string.IsNullOrWhiteSpace(httpMethod))
            throw new ArgumentException("Webhook HTTP method must not be empty.", nameof(httpMethod));

        RouteKey = routeKey.Trim();
        HttpMethod = httpMethod.Trim().ToUpperInvariant();
        SignatureValidation = signatureValidation;
        IdempotencyKeyExtraction = idempotencyKeyExtraction;
        _inputMappings.AddRange(inputMappings ?? []);
    }

    public string RouteKey { get; private set; }
    public string HttpMethod { get; private set; }
    public WebhookSignatureValidationConfiguration? SignatureValidation { get; private set; }
    public WebhookIdempotencyKeyExtractionConfiguration? IdempotencyKeyExtraction { get; private set; }
    public IReadOnlyList<WebhookInputMapping> InputMappings => _inputMappings;
}
