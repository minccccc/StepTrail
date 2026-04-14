namespace StepTrail.Shared.Definitions.Persistence;

public class ExecutableWorkflowDefinitionRecord
{
    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Version { get; set; }
    public WorkflowDefinitionStatus Status { get; set; }
    public string? Description { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ExecutableTriggerDefinitionRecord TriggerDefinition { get; set; } = null!;
    public ICollection<ExecutableStepDefinitionRecord> StepDefinitions { get; set; } = [];
}
