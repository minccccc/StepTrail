namespace StepTrail.Shared.Entities;

public class WorkflowDefinition
{
    public Guid Id { get; set; }

    /// <summary>
    /// Stable identifier used to reference this workflow type in code and API calls.
    /// Example: "user-onboarding", "order-fulfillment"
    /// </summary>
    public string Key { get; set; } = string.Empty;

    public int Version { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<WorkflowDefinitionStep> Steps { get; set; } = [];
    public ICollection<WorkflowInstance> Instances { get; set; } = [];
}
