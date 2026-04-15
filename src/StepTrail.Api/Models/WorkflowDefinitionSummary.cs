namespace StepTrail.Api.Models;

public sealed class WorkflowDefinitionSummary
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? TriggerType { get; init; }
    public string? Description { get; init; }
    public int StepCount { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}
