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
    public string? SourceTemplateKey { get; init; }
    public int? SourceTemplateVersion { get; init; }
    public int StepCount { get; init; }

    /// <summary>Ordered step summaries for shape preview (trigger → step1 → step2 → ...).</summary>
    public IReadOnlyList<WorkflowDefinitionStepSummary> Steps { get; init; } = [];

    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }
}

public sealed class WorkflowDefinitionStepSummary
{
    public string Key { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public int Order { get; init; }
}
