namespace StepTrail.Api.Models;

public sealed class CreateFromDescriptorRequest
{
    public string DescriptorKey { get; init; } = string.Empty;
    public int DescriptorVersion { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string TriggerType { get; init; } = string.Empty;
}
