namespace StepTrail.Api.Models;

public sealed class CreateBlankDefinitionRequest
{
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string TriggerType { get; init; } = string.Empty;
}
