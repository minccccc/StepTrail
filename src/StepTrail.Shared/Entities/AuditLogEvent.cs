namespace StepTrail.Shared.Entities;

/// <summary>
/// Structured audit log event recording who did what and when.
/// Covers authoring actions, execution milestones, and error/friction events.
/// </summary>
public class AuditLogEvent
{
    public Guid Id { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? WorkflowKey { get; set; }
    public Guid? WorkflowDefinitionId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public string? Status { get; set; }
    public string? TriggerType { get; set; }
    public string? StepType { get; set; }
    public string? Metadata { get; set; }
    public string? ActorId { get; set; }
}
