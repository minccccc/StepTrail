namespace StepTrail.Shared.Entities;

public class IdempotencyRecord
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>
    /// The workflow instance that was created for this key.
    /// </summary>
    public Guid WorkflowInstanceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public WorkflowInstance WorkflowInstance { get; set; } = null!;
}
