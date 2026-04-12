namespace StepTrail.Shared.Entities;

public class WorkflowEvent
{
    public Guid Id { get; set; }
    public Guid WorkflowInstanceId { get; set; }

    /// <summary>
    /// The step execution this event relates to. Null for instance-level events.
    /// </summary>
    public Guid? StepExecutionId { get; set; }

    /// <summary>
    /// Type of the event. Examples: WorkflowStarted, StepStarted, StepCompleted,
    /// StepFailed, StepRetryScheduled, WorkflowCompleted, WorkflowFailed
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Optional JSON payload with event-specific details.
    /// </summary>
    public string? Payload { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public WorkflowInstance WorkflowInstance { get; set; } = null!;
    public WorkflowStepExecution? StepExecution { get; set; }
}
