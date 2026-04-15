namespace StepTrail.Shared.Runtime;

public sealed class WorkflowStartResult
{
    public Guid Id { get; init; }
    public string WorkflowKey { get; init; } = string.Empty;
    public int Version { get; init; }
    public Guid TenantId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? ExternalKey { get; init; }
    public string? IdempotencyKey { get; init; }
    public Guid FirstStepExecutionId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public bool WasAlreadyStarted { get; init; }
    public bool Created { get; init; }
}
