namespace StepTrail.Shared.Definitions;

public sealed class SendWebhookStepConfiguration
{
    private SendWebhookStepConfiguration()
    {
        WebhookUrl = string.Empty;
        Method = "POST";
        Headers = new Dictionary<string, string>();
    }

    public SendWebhookStepConfiguration(
        string webhookUrl,
        string method = "POST",
        IReadOnlyDictionary<string, string>? headers = null,
        string? body = null)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            throw new ArgumentException("Webhook URL must not be empty.", nameof(webhookUrl));
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("Webhook method must not be empty.", nameof(method));

        WebhookUrl = webhookUrl.Trim();
        Method = method.Trim().ToUpperInvariant();
        Headers = NormalizeHeaders(headers, nameof(headers));
        Body = body;
    }

    public string WebhookUrl { get; private set; }
    public string Method { get; private set; }
    public Dictionary<string, string> Headers { get; private set; }
    public string? Body { get; private set; }

    private static Dictionary<string, string> NormalizeHeaders(
        IReadOnlyDictionary<string, string>? headers,
        string paramName)
    {
        var normalizedHeaders = new Dictionary<string, string>(StringComparer.Ordinal);

        if (headers is null)
            return normalizedHeaders;

        foreach (var header in headers)
        {
            if (string.IsNullOrWhiteSpace(header.Key))
                throw new ArgumentException("Header names must not be empty.", paramName);

            normalizedHeaders[header.Key.Trim()] = header.Value.Trim();
        }

        return normalizedHeaders;
    }
}
