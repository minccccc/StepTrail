namespace StepTrail.Api.Models;

/// <summary>
/// Structured step-by-step execution trail for a workflow instance.
/// Designed for Trail view rendering: steps are grouped with their attempt history,
/// latest outcome, and waiting/replay metadata.
/// </summary>
public sealed class WorkflowTrail
{
    public Guid InstanceId { get; init; }
    public string WorkflowKey { get; init; } = string.Empty;
    public int WorkflowVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>How the workflow was triggered.</summary>
    public TrailTriggerSummary? Trigger { get; init; }

    /// <summary>Steps in execution order, each with full attempt history.</summary>
    public IReadOnlyList<TrailStep> Steps { get; init; } = [];

    /// <summary>Replay events, if any replays occurred on this instance.</summary>
    public IReadOnlyList<TrailReplayEvent> ReplayEvents { get; init; } = [];
}

/// <summary>Summary of how the workflow was triggered.</summary>
public sealed class TrailTriggerSummary
{
    public string EventType { get; init; } = string.Empty;

    /// <summary>Trigger type (Webhook, Manual, Api, Schedule), if known.</summary>
    public string? TriggerType { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
    public string? Payload { get; init; }
}

/// <summary>
/// A single step in the trail, grouping all attempts for that step key.
/// The latest attempt determines the step's current status.
/// </summary>
public sealed class TrailStep
{
    public string StepKey { get; init; } = string.Empty;
    public string? StepType { get; init; }
    public int StepOrder { get; init; }

    /// <summary>Status of the most recent attempt.</summary>
    public string LatestStatus { get; init; } = string.Empty;

    /// <summary>Failure classification of the most recent attempt, if failed.</summary>
    public string? LatestFailureClassification { get; init; }

    /// <summary>Error message from the most recent attempt, if failed.</summary>
    public string? LatestError { get; init; }

    /// <summary>Output from the most recent attempt, if available.</summary>
    public string? LatestOutput { get; init; }

    /// <summary>When the step is in a Waiting state, the scheduled resume time.</summary>
    public DateTimeOffset? WaitingUntil { get; init; }

    /// <summary>When a retry is pending, the scheduled retry time.</summary>
    public DateTimeOffset? NextRetryAt { get; init; }

    /// <summary>All attempts for this step, ordered by attempt number.</summary>
    public IReadOnlyList<TrailStepAttempt> Attempts { get; init; } = [];
}

/// <summary>A single execution attempt within a step.</summary>
public sealed class TrailStepAttempt
{
    public Guid ExecutionId { get; init; }
    public int Attempt { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? FailureClassification { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Error { get; init; }
    public string? Output { get; init; }
}

/// <summary>A replay event that occurred on this workflow instance.</summary>
public sealed class TrailReplayEvent
{
    public DateTimeOffset OccurredAt { get; init; }
    public string? Payload { get; init; }
}
