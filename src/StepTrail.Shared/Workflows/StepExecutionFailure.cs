namespace StepTrail.Shared.Workflows;

/// <summary>
/// Safe-to-persist description of a failed step execution.
/// The runtime can later use the classification to drive retries,
/// trail rendering, and operator-facing diagnostics.
/// </summary>
public sealed class StepExecutionFailure
{
    public required StepExecutionFailureClassification Classification { get; init; }
    public required string Message { get; init; }
    public string? Details { get; init; }
    public TimeSpan? RetryDelayHint { get; init; }
}
