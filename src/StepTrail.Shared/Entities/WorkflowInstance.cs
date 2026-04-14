namespace StepTrail.Shared.Entities;

public class WorkflowInstance
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? WorkflowDefinitionId { get; set; }
    public Guid? ExecutableWorkflowDefinitionId { get; set; }
    public string? WorkflowDefinitionKey { get; set; }
    public int? WorkflowDefinitionVersion { get; set; }

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
    /// Raw inbound trigger payload captured at start time.
    /// For webhooks: { "body": {...}, "headers": {...}, "query": {...} }.
    /// Null for programmatic API starts where no external trigger context exists.
    /// Step executors must not mutate this value.
    /// </summary>
    public string? TriggerData { get; set; }

    /// <summary>
    /// Normalized workflow input derived from trigger data.
    /// This is what downstream steps receive and what {{input.*}} placeholders resolve against.
    /// </summary>
    public string? Input { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public ICollection<WorkflowStepExecution> StepExecutions { get; set; } = [];
    public ICollection<WorkflowEvent> Events { get; set; } = [];
}
