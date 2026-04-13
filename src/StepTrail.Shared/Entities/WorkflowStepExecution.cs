namespace StepTrail.Shared.Entities;

public class WorkflowStepExecution
{
    public Guid Id { get; set; }
    public Guid WorkflowInstanceId { get; set; }
    public Guid WorkflowDefinitionStepId { get; set; }

    /// <summary>
    /// Denormalized step key for easier querying without joins.
    /// </summary>
    public string StepKey { get; set; } = string.Empty;

    public WorkflowStepExecutionStatus Status { get; set; }

    /// <summary>
    /// Current attempt number. Starts at 1.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>
    /// JSON input passed to the step handler.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// JSON output produced by the step handler on success.
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// Error message captured on failure.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// When this execution is eligible to be picked up by a worker.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>
    /// When a worker locked this execution for processing.
    /// </summary>
    public DateTimeOffset? LockedAt { get; set; }

    /// <summary>
    /// Identifier of the worker instance that locked this execution.
    /// </summary>
    public string? LockedBy { get; set; }

    /// <summary>
    /// When the worker lock expires. Set at claim time based on the default lock window.
    /// Executions still Running past this timestamp are treated as orphaned and requeued.
    /// </summary>
    public DateTimeOffset? LockExpiresAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public WorkflowInstance WorkflowInstance { get; set; } = null!;
    public WorkflowDefinitionStep WorkflowDefinitionStep { get; set; } = null!;
    public ICollection<WorkflowEvent> Events { get; set; } = [];
}
