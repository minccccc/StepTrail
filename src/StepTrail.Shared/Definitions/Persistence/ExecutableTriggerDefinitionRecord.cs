namespace StepTrail.Shared.Definitions.Persistence;

public class ExecutableTriggerDefinitionRecord
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public TriggerType Type { get; set; }
    public string Configuration { get; set; } = string.Empty;

    public ExecutableWorkflowDefinitionRecord WorkflowDefinition { get; set; } = null!;
}
