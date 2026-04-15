namespace StepTrail.Shared.Definitions.Persistence;

public class ExecutableStepDefinitionRecord
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }
    public string Key { get; set; } = string.Empty;
    public int Order { get; set; }
    public StepType Type { get; set; }
    public string Configuration { get; set; } = string.Empty;
    public string? RetryPolicyOverrideKey { get; set; }
    public string? RetryPolicyJson { get; set; }

    public ExecutableWorkflowDefinitionRecord WorkflowDefinition { get; set; } = null!;
}
