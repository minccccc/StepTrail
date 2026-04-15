using System.Net;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker.Handlers;

/// <summary>
/// Centralizes HTTP response and transport failure classification so the executor
/// does not scatter retryability rules across request-building logic.
/// Default behavior:
/// 2xx = success
/// 408, 429, and 5xx = retryable
/// everything else = permanent failure
/// </summary>
public sealed class HttpResponseClassifier : IHttpResponseClassifier
{
    public HttpResponseClassificationResult ClassifyResponse(
        HttpStatusCode statusCode,
        HttpResponseClassificationConfiguration? configuration = null)
    {
        var numericStatusCode = (int)statusCode;

        if (IsSuccess(numericStatusCode, configuration))
            return HttpResponseClassificationResult.Success();

        if (IsRetryable(numericStatusCode, configuration))
            return HttpResponseClassificationResult.Failure(StepExecutionFailureClassification.TransientFailure);

        return HttpResponseClassificationResult.Failure(StepExecutionFailureClassification.PermanentFailure);
    }

    public HttpResponseClassificationResult ClassifyTransportFailure() =>
        HttpResponseClassificationResult.Failure(StepExecutionFailureClassification.TransientFailure);

    public HttpResponseClassificationResult ClassifyTimeout() =>
        HttpResponseClassificationResult.Failure(StepExecutionFailureClassification.TransientFailure);

    private static bool IsSuccess(int statusCode, HttpResponseClassificationConfiguration? configuration)
    {
        if (configuration is not null && configuration.SuccessStatusCodes.Count > 0)
            return configuration.SuccessStatusCodes.Contains(statusCode);

        return statusCode >= 200 && statusCode <= 299;
    }

    private static bool IsRetryable(int statusCode, HttpResponseClassificationConfiguration? configuration)
    {
        if (configuration is not null && configuration.RetryableStatusCodes.Count > 0)
            return configuration.RetryableStatusCodes.Contains(statusCode);

        return statusCode is 408 or 429 || (statusCode >= 500 && statusCode <= 599);
    }
}
