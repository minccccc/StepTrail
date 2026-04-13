namespace StepTrail.Shared.Entities;

/// <summary>
/// Defines an interval-based recurring trigger for a workflow definition.
/// The scheduler creates a new WorkflowInstance every IntervalSeconds seconds.
/// </summary>
public class RecurringWorkflowSchedule
{
    public Guid Id { get; set; }

    /// <summary>
    /// The workflow definition to instantiate on each firing.
    /// </summary>
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Tenant under which new instances are created.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// How often to fire, in seconds.
    /// </summary>
    public int IntervalSeconds { get; set; }

    /// <summary>
    /// When false the schedule is skipped during dispatching.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Optional JSON payload forwarded as input to each created instance.
    /// </summary>
    public string? Input { get; set; }

    /// <summary>
    /// Timestamp of the most recent dispatch. Null before the first firing.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; set; }

    /// <summary>
    /// When the schedule is next due. Dispatcher claims rows where this <= now.
    /// </summary>
    public DateTimeOffset NextRunAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public Tenant Tenant { get; set; } = null!;
}
