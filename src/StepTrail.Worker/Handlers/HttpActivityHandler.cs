using System.Text;
using System.Text.Json;
using System.Net.Http.Headers;
using StepTrail.Shared.Runtime.OutputModels;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

/// <summary>
/// Step executor that performs an outbound HTTP request.
/// Configuration is read from StepExecutionRequest.StepConfiguration as HttpActivityConfig JSON.
/// All {{input.*}}, {{steps.*.output.*}}, and {{secrets.*}} placeholders in URL, header values,
/// and body are resolved through the shared placeholder infrastructure before the request is sent.
/// Expected execution failures are returned as classified StepExecutionResult values so the
/// runtime can persist output and reason about retryability consistently.
/// </summary>
public sealed class HttpActivityHandler : IStepExecutor
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpResponseClassifier _responseClassifier;
    private readonly ILogger<HttpActivityHandler> _logger;

    public HttpActivityHandler(
        IHttpClientFactory httpClientFactory,
        IHttpResponseClassifier responseClassifier,
        ILogger<HttpActivityHandler> logger)
    {
        _httpClientFactory = httpClientFactory;
        _responseClassifier = responseClassifier;
        _logger = logger;
    }

    public async Task<StepExecutionResult> ExecuteAsync(StepExecutionRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.StepConfiguration))
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}' uses HttpActivityHandler but has no configuration.");
        }

        HttpActivityConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<HttpActivityConfig>(
                request.StepConfiguration,
                JsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': failed to deserialize HttpActivityConfig.",
                details: ex.Message);
        }

        if (config is null)
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': failed to deserialize HttpActivityConfig.");
        }

        if (string.IsNullOrWhiteSpace(config.Url))
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': HttpActivityConfig.Url is required.");
        }

        var resolvedUrl = request.ResolveTemplate(config.Url, "URL");
        if (!resolvedUrl.IsSuccess)
            return StepExecutionResult.InputResolutionFailure(resolvedUrl.Error!);

        HttpRequestMessage outboundRequest;
        try
        {
            outboundRequest = new HttpRequestMessage(new HttpMethod(config.Method), resolvedUrl.Value);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or UriFormatException)
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': HTTP request could not be constructed.",
                details: ex.Message);
        }

        using var requestMessage = outboundRequest;

        if (config.Headers is not null)
        {
            foreach (var (key, value) in config.Headers)
            {
                var resolvedHeader = request.ResolveTemplate(value, $"header '{key}'");
                if (!resolvedHeader.IsSuccess)
                    return StepExecutionResult.InputResolutionFailure(resolvedHeader.Error!);

                requestMessage.Headers.TryAddWithoutValidation(key, resolvedHeader.Value);
            }
        }

        var rawBody = config.Body ?? request.Input;
        if (!string.IsNullOrEmpty(rawBody))
        {
            var resolvedBody = request.ResolveTemplate(rawBody, "body");
            if (!resolvedBody.IsSuccess)
                return StepExecutionResult.InputResolutionFailure(resolvedBody.Error!);

            requestMessage.Content = new StringContent(resolvedBody.Value!, Encoding.UTF8, "application/json");
        }

        _logger.LogInformation(
            "Step '{StepKey}' executing HTTP {Method} {Url}",
            request.StepKey, config.Method, config.Url);

        HttpResponseMessage response;
        string responseBody;
        CancellationTokenSource? timeoutCts = null;
        var httpToken = ct;

        try
        {
            if (config.TimeoutSeconds.HasValue)
            {
                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds.Value));
                httpToken = timeoutCts.Token;
            }

            var httpClient = _httpClientFactory.CreateClient("HttpActivity");
            response = await httpClient.SendAsync(requestMessage, httpToken);
            responseBody = await response.Content.ReadAsStringAsync(httpToken);
        }
        catch (HttpRequestException ex)
        {
            var classification = _responseClassifier.ClassifyTransportFailure();
            var transportFailureOutput = CreateTransportFailureOutput(config.Method, resolvedUrl.Value!);
            return ToFailureResult(
                classification,
                $"HTTP {config.Method} {resolvedUrl.Value} failed before a response was received.",
                transportFailureOutput,
                ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
        {
            var classification = _responseClassifier.ClassifyTimeout();
            var timeoutOutput = CreateTransportFailureOutput(config.Method, resolvedUrl.Value!);
            return ToFailureResult(
                classification,
                $"HTTP {config.Method} {resolvedUrl.Value} timed out after {config.TimeoutSeconds} second(s).",
                timeoutOutput);
        }
        finally
        {
            timeoutCts?.Dispose();
        }

        var output = JsonSerializer.Serialize(
            CreateOutput(
                statusCode: (int)response.StatusCode,
                body: CreateBodyJsonValue(responseBody, response.Content.Headers.ContentType),
                bodyText: responseBody,
                contentType: response.Content.Headers.ContentType?.MediaType,
                headers: CaptureResponseHeaders(response),
                requestMethod: config.Method,
                requestUrl: resolvedUrl.Value!),
            JsonSerializerOptions);

        var classificationResult = _responseClassifier.ClassifyResponse(
            response.StatusCode,
            config.ResponseClassification);

        if (!classificationResult.IsSuccess)
        {
            _logger.LogWarning(
                "Step '{StepKey}' HTTP {Method} {Url} returned {StatusCode} and was classified as {Classification}",
                request.StepKey,
                config.Method,
                config.Url,
                (int)response.StatusCode,
                classificationResult.FailureClassification);

            return ToFailureResult(
                classificationResult,
                $"HTTP {config.Method} {config.Url} returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                output);
        }

        _logger.LogInformation(
            "Step '{StepKey}' HTTP {Method} {Url} succeeded ({StatusCode})",
            request.StepKey, config.Method, config.Url, (int)response.StatusCode);

        return StepExecutionResult.Success(output);
    }

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

    private static JsonElement CreateBodyJsonValue(string responseBody, MediaTypeHeaderValue? contentType)
    {
        if (LooksLikeJsonContentType(contentType) && TryParseJson(responseBody, out var parsedJsonBody))
            return parsedJsonBody;

        return JsonSerializer.SerializeToElement(responseBody);
    }

    private static bool LooksLikeJsonContentType(MediaTypeHeaderValue? contentType)
    {
        var mediaType = contentType?.MediaType;
        if (string.IsNullOrWhiteSpace(mediaType))
            return false;

        return mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseJson(string responseBody, out JsonElement parsedJsonBody)
    {
        try
        {
            using var document = JsonDocument.Parse(responseBody);
            parsedJsonBody = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            parsedJsonBody = default;
            return false;
        }
    }

    private static StepExecutionResult ToFailureResult(
        HttpResponseClassificationResult classification,
        string message,
        string? output = null,
        string? details = null) =>
        classification.FailureClassification switch
        {
            StepExecutionFailureClassification.TransientFailure =>
                StepExecutionResult.TransientFailure(message, output, details),
            StepExecutionFailureClassification.PermanentFailure =>
                StepExecutionResult.PermanentFailure(message, output, details),
            _ => throw new InvalidOperationException(
                $"HTTP response classifier returned unsupported failure classification '{classification.FailureClassification}'.")
        };

    private static string CreateTransportFailureOutput(string method, string requestUrl) =>
        JsonSerializer.Serialize(
            CreateOutput(
                statusCode: 0,
                body: JsonSerializer.SerializeToElement(string.Empty),
                bodyText: string.Empty,
                contentType: null,
                headers: null,
                requestMethod: method,
                requestUrl: requestUrl),
            JsonSerializerOptions);

    private static HttpRequestStepOutput CreateOutput(
        int statusCode,
        JsonElement body,
        string bodyText,
        string? contentType,
        IReadOnlyDictionary<string, string>? headers,
        string requestMethod,
        string requestUrl) =>
        new()
        {
            StatusCode = statusCode,
            Body = body,
            BodyText = bodyText,
            ContentType = contentType,
            Headers = headers,
            RequestMethod = requestMethod,
            RequestUrl = requestUrl
        };
}
