namespace StepTrail.Api.Models;

public sealed class CloneWorkflowDefinitionRequest
{
    public Guid TemplateId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
}
