namespace StepTrail.Worker.Alerts;

/// <summary>
/// Supported alert conditions. Each value maps to a specific runtime event
/// that can trigger an alert notification through configured channels.
/// </summary>
public enum AlertRuleType
{
    /// <summary>
    /// Workflow entered the Failed terminal state — either because all retry
    /// attempts were exhausted or because a non-retryable failure occurred.
    /// </summary>
    WorkflowFailed = 1,

    /// <summary>
    /// A step execution was detected as stuck (orphaned) because its lock
    /// expired without completion, typically due to a worker crash.
    /// </summary>
    StuckExecutionDetected = 2
}
