namespace StepTrail.Api.Models;

public sealed class WorkflowInstanceDetail
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string WorkflowKey { get; init; } = string.Empty;
    public int WorkflowVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    /// <summary>How this workflow was triggered (Webhook, Manual, Api, Schedule), if known.</summary>
    public string? TriggerType { get; init; }
    public string? ExternalKey { get; init; }
    public string? IdempotencyKey { get; init; }

    /// <summary>Normalized workflow input derived from trigger data.</summary>
    public string? Input { get; init; }

    /// <summary>Raw inbound trigger payload captured at start time.</summary>
    public string? TriggerData { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Whether the operator can invoke the retry action.</summary>
    public bool CanRetry { get; init; }

    /// <summary>Whether the operator can invoke the replay action.</summary>
    public bool CanReplay { get; init; }

    /// <summary>Whether the operator can invoke the cancel action.</summary>
    public bool CanCancel { get; init; }

    /// <summary>Whether the operator can invoke the archive action.</summary>
    public bool CanArchive { get; init; }

    public IReadOnlyList<StepExecutionSummary> StepExecutions { get; init; } = [];
}

public sealed class StepExecutionSummary
{
    public Guid Id { get; init; }
    public string StepKey { get; init; } = string.Empty;
    public string? StepType { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? FailureClassification { get; init; }
    public int Attempt { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
