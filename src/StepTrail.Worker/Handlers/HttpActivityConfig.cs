namespace StepTrail.Worker.Handlers;

/// <summary>
/// Configuration for HttpActivityHandler.
/// Stored as JSON in WorkflowDefinitionStep.Config and passed to the handler via StepContext.Config.
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
}
