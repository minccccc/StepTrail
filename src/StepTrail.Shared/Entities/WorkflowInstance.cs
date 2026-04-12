namespace StepTrail.Shared.Entities;

public class WorkflowInstance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Optional caller-provided correlation identifier.
    /// </summary>
    public string? ExternalKey { get; set; }

    /// <summary>
    /// Idempotency key supplied by the caller at start time.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public WorkflowInstanceStatus Status { get; set; }

    /// <summary>
    /// JSON payload provided when the workflow was started.
    /// </summary>
    public string? Input { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public ICollection<WorkflowStepExecution> StepExecutions { get; set; } = [];
    public ICollection<WorkflowEvent> Events { get; set; } = [];
}
