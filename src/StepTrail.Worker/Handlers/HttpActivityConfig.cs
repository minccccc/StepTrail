using StepTrail.Shared.Definitions;

namespace StepTrail.Worker.Handlers;

/// <summary>
/// Configuration for HttpActivityHandler.
/// Stored as JSON in WorkflowDefinitionStep.Config and passed to the executor via
/// StepExecutionRequest.StepConfiguration.
/// </summary>
public sealed class HttpActivityConfig
{
    /// <summary>
    /// The URL to call. Required.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method. Defaults to POST.
    /// </summary>
    public string Method { get; set; } = "POST";

    /// <summary>
    /// Optional headers to include in the request.
    /// </summary>
    public Dictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Optional static request body.
    /// When null, the step's input (previous step output) is used as the body.
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Optional step-local request timeout in seconds.
    /// When specified, the executor cancels the outbound request if it exceeds this duration.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Optional override rules for classifying HTTP responses as success, retryable failure,
    /// or permanent failure. When null, the runtime uses the default classifier behavior.
    /// </summary>
    public HttpResponseClassificationConfiguration? ResponseClassification { get; set; }
}
