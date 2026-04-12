namespace StepTrail.Api.Models;

public sealed class StartWorkflowResponse
{
    public Guid Id { get; set; }
    public string WorkflowKey { get; set; } = string.Empty;
    public int Version { get; set; }
    public Guid TenantId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ExternalKey { get; set; }
    public string? IdempotencyKey { get; set; }
    public Guid FirstStepExecutionId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// True when the response is for a previously created instance returned via idempotency.
    /// </summary>
    public bool WasAlreadyStarted { get; set; }
}
