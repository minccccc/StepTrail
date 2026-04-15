using System.Text;
using System.Text.Json;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Runtime.OutputModels;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

public sealed class SendWebhookStepExecutor : IStepExecutor
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHttpResponseClassifier _responseClassifier;
    private readonly ILogger<SendWebhookStepExecutor> _logger;

    public SendWebhookStepExecutor(
        IHttpClientFactory httpClientFactory,
        IHttpResponseClassifier responseClassifier,
        ILogger<SendWebhookStepExecutor> logger)
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
                $"Step '{request.StepKey}' uses SendWebhookStepExecutor but has no configuration.");
        }

        SendWebhookStepConfigurationSnapshot? config;
        try
        {
            config = JsonSerializer.Deserialize<SendWebhookStepConfigurationSnapshot>(
                request.StepConfiguration,
                JsonSerializerOptions);
        }
        catch (JsonException ex)
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': failed to deserialize SendWebhookStepConfiguration.",
                details: ex.Message);
        }

        if (config is null)
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': failed to deserialize SendWebhookStepConfiguration.");
        }

        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': outbound webhook destination URL is required.");
        }

        if (!string.Equals(config.Method, "POST", StringComparison.OrdinalIgnoreCase))
        {
            return StepExecutionResult.InvalidConfiguration(
                $"Step '{request.StepKey}': outbound webhook method must be POST.");
        }

        var resolvedUrl = request.ResolveTemplate(config.WebhookUrl, "destination URL");
        if (!resolvedUrl.IsSuccess)
            return StepExecutionResult.InputResolutionFailure(resolvedUrl.Error!);

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, resolvedUrl.Value);

        foreach (var (key, value) in config.Headers)
        {
            var resolvedHeader = request.ResolveTemplate(value, $"header '{key}'");
            if (!resolvedHeader.IsSuccess)
                return StepExecutionResult.InputResolutionFailure(resolvedHeader.Error!);

            requestMessage.Headers.TryAddWithoutValidation(key, resolvedHeader.Value);
        }

        var rawBody = config.Body ?? "{}";
        var resolvedBody = request.ResolveTemplate(rawBody, "payload");
        if (!resolvedBody.IsSuccess)
            return StepExecutionResult.InputResolutionFailure(resolvedBody.Error!);

        requestMessage.Content = new StringContent(resolvedBody.Value!, Encoding.UTF8, "application/json");

        var attemptedAtUtc = DateTimeOffset.UtcNow;
        _logger.LogInformation(
            "Step '{StepKey}' sending outbound webhook POST {Destination}",
            request.StepKey,
            resolvedUrl.Value);

        HttpResponseMessage response;
        string responseBodyText;
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
            responseBodyText = await response.Content.ReadAsStringAsync(httpToken);
        }
        catch (HttpRequestException ex)
        {
            var output = CreateOutput(
                delivered: false,
                destination: resolvedUrl.Value!,
                attemptedAtUtc: attemptedAtUtc,
                statusCode: null,
                responseBodyText: null);
            var classification = _responseClassifier.ClassifyTransportFailure();

            return ToFailureResult(
                classification,
                $"Webhook POST {resolvedUrl.Value} failed before a response was received.",
                output,
                ex.Message);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts?.IsCancellationRequested == true)
        {
            var output = CreateOutput(
                delivered: false,
                destination: resolvedUrl.Value!,
                attemptedAtUtc: attemptedAtUtc,
                statusCode: null,
                responseBodyText: null);
            var classification = _responseClassifier.ClassifyTimeout();

            return ToFailureResult(
                classification,
                $"Webhook POST {resolvedUrl.Value} timed out after {config.TimeoutSeconds} second(s).",
                output);
        }
        finally
        {
            timeoutCts?.Dispose();
        }

        var responseClassification = _responseClassifier.ClassifyResponse(response.StatusCode);
        var outputJson = CreateOutput(
            delivered: responseClassification.IsSuccess,
            destination: resolvedUrl.Value!,
            attemptedAtUtc: attemptedAtUtc,
            statusCode: (int)response.StatusCode,
            responseBodyText: string.IsNullOrWhiteSpace(responseBodyText) ? null : responseBodyText);

        if (!responseClassification.IsSuccess)
        {
            _logger.LogWarning(
                "Step '{StepKey}' outbound webhook POST {Destination} returned {StatusCode} and was classified as {Classification}",
                request.StepKey,
                resolvedUrl.Value,
                (int)response.StatusCode,
                responseClassification.FailureClassification);

            return ToFailureResult(
                responseClassification,
                $"Webhook POST {resolvedUrl.Value} returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                outputJson);
        }

        _logger.LogInformation(
            "Step '{StepKey}' outbound webhook POST {Destination} succeeded ({StatusCode})",
            request.StepKey,
            resolvedUrl.Value,
            (int)response.StatusCode);

        return StepExecutionResult.Success(outputJson);
    }

    private sealed class SendWebhookStepConfigurationSnapshot
    {
        public string WebhookUrl { get; set; } = string.Empty;
        public string Method { get; set; } = "POST";
        public Dictionary<string, string> Headers { get; set; } = new(StringComparer.Ordinal);
        public string? Body { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    private static string CreateOutput(
        bool delivered,
        string destination,
        DateTimeOffset attemptedAtUtc,
        int? statusCode,
        string? responseBodyText) =>
        JsonSerializer.Serialize(
            new SendWebhookStepOutput
            {
                Delivered = delivered,
                StatusCode = statusCode,
                Destination = destination,
                AttemptedAtUtc = attemptedAtUtc,
                ResponseBodyText = responseBodyText
            },
            JsonSerializerOptions);

    private static StepExecutionResult ToFailureResult(
        HttpResponseClassificationResult classification,
        string message,
        string output,
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
}
