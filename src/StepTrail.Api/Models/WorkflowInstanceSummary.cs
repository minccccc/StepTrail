namespace StepTrail.Api.Models;

public sealed class WorkflowInstanceSummary
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public string WorkflowKey { get; init; } = string.Empty;
    public int WorkflowVersion { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ExternalKey { get; init; }
    public string? IdempotencyKey { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>Step key of the most recently created step execution, or null if none yet.</summary>
    public string? CurrentStep { get; init; }
}
