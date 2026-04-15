namespace StepTrail.Shared.Definitions;

public sealed class HttpRequestStepConfiguration
{
    private HttpRequestStepConfiguration()
    {
        Url = string.Empty;
        Method = "POST";
        Headers = new Dictionary<string, string>();
    }

    public HttpRequestStepConfiguration(
        string url,
        string method = "POST",
        IReadOnlyDictionary<string, string>? headers = null,
        string? body = null,
        int? timeoutSeconds = null,
        HttpResponseClassificationConfiguration? responseClassification = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("HTTP request URL must not be empty.", nameof(url));
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("HTTP request method must not be empty.", nameof(method));
        if (timeoutSeconds is < 1)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "HTTP request timeout must be 1 second or greater when specified.");

        Url = url.Trim();
        Method = method.Trim().ToUpperInvariant();
        Headers = NormalizeHeaders(headers, nameof(headers));
        Body = body;
        TimeoutSeconds = timeoutSeconds;
        ResponseClassification = responseClassification;
    }

    public string Url { get; private set; }
    public string Method { get; private set; }
    public Dictionary<string, string> Headers { get; private set; }
    public string? Body { get; private set; }
    public int? TimeoutSeconds { get; private set; }
    public HttpResponseClassificationConfiguration? ResponseClassification { get; private set; }

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
