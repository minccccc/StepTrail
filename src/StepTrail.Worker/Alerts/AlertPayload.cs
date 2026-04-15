namespace StepTrail.Worker.Alerts;

/// <summary>
/// Normalized data carried in every alert notification.
/// Contains enough context for external receivers (webhook, email) to be useful
/// without needing to call back into the API for details.
/// </summary>
public sealed class AlertPayload
{
    /// <summary>
    /// Alert condition that triggered this notification (e.g. "WorkflowFailed", "StuckExecutionDetected").
    /// </summary>
    public string AlertType { get; init; } = string.Empty;

    public Guid WorkflowInstanceId { get; init; }

    /// <summary>
    /// The workflow definition key, e.g. "user-onboarding".
    /// </summary>
    public string WorkflowKey { get; init; } = string.Empty;

    /// <summary>
    /// The workflow definition version at the time of the alert, if available.
    /// </summary>
    public int? WorkflowVersion { get; init; }

    /// <summary>
    /// Current workflow instance status (e.g. "Failed", "AwaitingRetry").
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// The step key where the failure or condition occurred.
    /// </summary>
    public string StepKey { get; init; } = string.Empty;

    /// <summary>
    /// Attempt number at the time of the alert.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// Human-readable summary of what happened.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Detailed error message or timeout reason, if available.
    /// </summary>
    public string? Error { get; init; }

    public DateTimeOffset OccurredAtUtc { get; init; }
}
