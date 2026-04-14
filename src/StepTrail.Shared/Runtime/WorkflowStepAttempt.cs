using StepTrail.Shared.Entities;

namespace StepTrail.Shared.Runtime;

/// <summary>
/// Data from one execution attempt for a workflow step.
/// Corresponds to one WorkflowStepExecution row.
/// </summary>
public sealed class WorkflowStepAttempt
{
    public WorkflowStepAttempt(
        int attempt,
        WorkflowStepExecutionStatus status,
        string? output,
        string? error,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt)
    {
        Attempt = attempt;
        Status = status;
        Output = output;
        Error = error;
        StartedAt = startedAt;
        CompletedAt = completedAt;
    }

    /// <summary>1-based attempt number.</summary>
    public int Attempt { get; }

    public WorkflowStepExecutionStatus Status { get; }

    /// <summary>JSON output produced by the handler, if the attempt succeeded.</summary>
    public string? Output { get; }

    /// <summary>Error message captured if the attempt failed.</summary>
    public string? Error { get; }

    public DateTimeOffset? StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; }
}
