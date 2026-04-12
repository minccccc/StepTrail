namespace StepTrail.Api.Models;

public sealed class WorkflowInstanceDetail
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string WorkflowKey { get; init; } = string.Empty;
    public int WorkflowVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ExternalKey { get; init; }
    public string? IdempotencyKey { get; init; }
    public string? Input { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyList<StepExecutionSummary> StepExecutions { get; init; } = [];
}

public sealed class StepExecutionSummary
{
    public Guid Id { get; init; }
    public string StepKey { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public int Attempt { get; init; }
    public DateTimeOffset ScheduledAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string? Output { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
