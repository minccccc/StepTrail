namespace StepTrail.Shared.Definitions;

public sealed class WebhookSignatureValidationConfiguration
{
    private WebhookSignatureValidationConfiguration()
    {
        HeaderName = string.Empty;
        SecretName = string.Empty;
    }

    public WebhookSignatureValidationConfiguration(
        string headerName,
        string secretName,
        WebhookSignatureAlgorithm algorithm,
        string? signaturePrefix = null)
    {
        if (string.IsNullOrWhiteSpace(headerName))
            throw new ArgumentException("Webhook signature header name must not be empty.", nameof(headerName));
        if (string.IsNullOrWhiteSpace(secretName))
            throw new ArgumentException("Webhook signature secret name must not be empty.", nameof(secretName));
        if (!Enum.IsDefined(algorithm))
            throw new ArgumentOutOfRangeException(nameof(algorithm), $"Webhook signature algorithm '{algorithm}' is not supported.");

        HeaderName = headerName.Trim();
        SecretName = secretName.Trim();
        Algorithm = algorithm;
        SignaturePrefix = string.IsNullOrWhiteSpace(signaturePrefix)
            ? null
            : signaturePrefix.Trim();
    }

    public string HeaderName { get; private set; }
    public string SecretName { get; private set; }
    public WebhookSignatureAlgorithm Algorithm { get; private set; }
    public string? SignaturePrefix { get; private set; }
}
