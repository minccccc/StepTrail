using System.Text;
using System.Text.Json;
using StepTrail.Shared.Runtime.OutputModels;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

/// <summary>
/// Step handler that executes an outbound HTTP request.
/// Configuration (URL, method, headers, body) is read from StepContext.Config as HttpActivityConfig JSON.
/// All {{input.*}}, {{steps.*.output.*}}, and {{secrets.*}} placeholders in URL, header values, and body
/// are resolved via StepContext.Resolve() before the request is made.
/// On non-2xx response the handler throws, which causes the worker to fail the step and apply the retry policy.
/// On success the response status code, headers, and body are stored as the step's JSON output.
/// </summary>
public sealed class HttpActivityHandler : IStepHandler
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HttpActivityHandler> _logger;

    public HttpActivityHandler(
        IHttpClientFactory httpClientFactory,
        ILogger<HttpActivityHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.Config))
            throw new InvalidOperationException(
                $"Step '{context.StepKey}' uses HttpActivityHandler but has no config.");

        var config = JsonSerializer.Deserialize<HttpActivityConfig>(
                context.Config,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException(
                $"Step '{context.StepKey}': failed to deserialize HttpActivityConfig.");

        if (string.IsNullOrWhiteSpace(config.Url))
            throw new InvalidOperationException(
                $"Step '{context.StepKey}': HttpActivityConfig.Url is required.");

        // Resolve all {{input.*}}, {{steps.*.output.*}}, and {{secrets.*}} placeholders
        // in URL, headers, and body before building the request.
        var url = context.Resolve(config.Url, "URL");

        using var request = new HttpRequestMessage(new HttpMethod(config.Method), url);

        if (config.Headers is not null)
            foreach (var (key, value) in config.Headers)
            {
                var resolvedValue = context.Resolve(value, $"header '{key}'");
                request.Headers.TryAddWithoutValidation(key, resolvedValue);
            }

        // Use config body if provided; otherwise forward the step's input as the body.
        var rawBody = config.Body ?? context.Input;
        var body = string.IsNullOrEmpty(rawBody) ? null : context.Resolve(rawBody, "body");
        if (body is not null)
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        _logger.LogInformation(
            "Step '{StepKey}' executing HTTP {Method} {Url}",
            context.StepKey, config.Method, config.Url);

        var httpClient = _httpClientFactory.CreateClient("HttpActivity");
        var response = await httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        // Capture status + headers + body as structured output regardless of success/failure.
        var output = JsonSerializer.Serialize(new HttpRequestStepOutput
        {
            StatusCode = (int)response.StatusCode,
            Body       = responseBody,
            Headers    = CaptureResponseHeaders(response)
        });

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Step '{StepKey}' HTTP {Method} {Url} returned {StatusCode}",
                context.StepKey, config.Method, config.Url, (int)response.StatusCode);

            // HttpActivityException carries the response output so the processor can persist
            // it on the failed step execution — useful for diagnosing what the remote server said.
            throw new HttpActivityException(
                $"HTTP {config.Method} {config.Url} returned {(int)response.StatusCode} " +
                $"{response.ReasonPhrase}.",
                output);
        }

        _logger.LogInformation(
            "Step '{StepKey}' HTTP {Method} {Url} succeeded ({StatusCode})",
            context.StepKey, config.Method, config.Url, (int)response.StatusCode);

        return StepResult.Success(output);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Collects response headers (both headers and content headers) into a flat dictionary.
    /// Header names are lower-cased; multi-value headers are joined with ", " per RFC 7230.
    /// </summary>
    private static IReadOnlyDictionary<string, string> CaptureResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, values) in response.Headers)
            headers[key.ToLowerInvariant()] = string.Join(", ", values);

        foreach (var (key, values) in response.Content.Headers)
            headers[key.ToLowerInvariant()] = string.Join(", ", values);

        return headers;
    }
}
