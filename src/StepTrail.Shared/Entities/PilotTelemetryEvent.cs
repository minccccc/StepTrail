namespace StepTrail.Shared.Entities;

/// <summary>
/// Structured telemetry event for pilot usage instrumentation.
/// Records product-relevant milestones (authoring, execution, errors) to help
/// understand how the product is actually being used and where users get stuck.
/// </summary>
public class PilotTelemetryEvent
{
    public Guid Id { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string? WorkflowKey { get; set; }
    public Guid? WorkflowDefinitionId { get; set; }
    public Guid? WorkflowInstanceId { get; set; }
    public string? TriggerType { get; set; }
    public string? StepType { get; set; }
    public string? Metadata { get; set; }
    public string? ActorId { get; set; }
}
