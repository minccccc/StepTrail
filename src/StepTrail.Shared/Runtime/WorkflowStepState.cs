using StepTrail.Shared.Entities;

namespace StepTrail.Shared.Runtime;

/// <summary>
/// Current state of one workflow step, aggregated across all execution attempts.
/// Accessible under WorkflowState.Steps[stepKey].
/// </summary>
public sealed class WorkflowStepState
{
    public WorkflowStepState(
        string stepKey,
        WorkflowStepExecutionStatus status,
        string? output,
        string? error,
        IReadOnlyList<WorkflowStepAttempt> attempts)
    {
        StepKey = stepKey;
        Status = status;
        Output = output;
        Error = error;
        Attempts = attempts;
    }

    public string StepKey { get; }

    /// <summary>Status of the most recent attempt for this step.</summary>
    public WorkflowStepExecutionStatus Status { get; }

    /// <summary>
    /// JSON output of the most recent completed attempt.
    /// This is what {{steps.step_name.output.*}} placeholders resolve against.
    /// Null if the step has not completed successfully on any attempt.
    /// </summary>
    public string? Output { get; }

    /// <summary>Error message of the most recent failed attempt, if any.</summary>
    public string? Error { get; }

    /// <summary>All execution attempts ordered by attempt number ascending.</summary>
    public IReadOnlyList<WorkflowStepAttempt> Attempts { get; }
}
