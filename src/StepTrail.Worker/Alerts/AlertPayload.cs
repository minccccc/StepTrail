namespace StepTrail.Worker.Alerts;

/// <summary>
/// Data carried in every alert notification.
/// </summary>
public sealed class AlertPayload
{
    /// <summary>
    /// "WorkflowFailed" or "StepOrphaned".
    /// </summary>
    public string AlertType { get; init; } = string.Empty;

    public Guid WorkflowInstanceId { get; init; }

    /// <summary>
    /// The workflow definition key, e.g. "user-onboarding".
    /// </summary>
    public string WorkflowKey { get; init; } = string.Empty;

    /// <summary>
    /// The step key that triggered the alert.
    /// </summary>
    public string StepKey { get; init; } = string.Empty;

    /// <summary>
    /// Attempt number at the time of the alert.
    /// </summary>
    public int Attempt { get; init; }

    /// <summary>
    /// Error message or timeout reason.
    /// </summary>
    public string? Error { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}
