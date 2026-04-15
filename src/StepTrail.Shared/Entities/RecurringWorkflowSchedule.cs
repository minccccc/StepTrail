namespace StepTrail.Shared.Entities;

/// <summary>
/// Defines a recurring trigger for a workflow definition.
/// The scheduler creates a new WorkflowInstance on either an interval or cron expression.
/// </summary>
public class RecurringWorkflowSchedule
{
    public Guid Id { get; set; }

    /// <summary>
    /// The legacy code-first workflow definition to instantiate on each firing.
    /// Null for executable definition schedules.
    /// </summary>
    public Guid? WorkflowDefinitionId { get; set; }

    /// <summary>
    /// The executable workflow key to instantiate on each firing.
    /// Null for legacy code-first schedules.
    /// </summary>
    public string? ExecutableWorkflowKey { get; set; }

    /// <summary>
    /// Tenant under which new instances are created.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// How often to fire, in seconds.
    /// Null when this schedule uses a cron expression.
    /// </summary>
    public int? IntervalSeconds { get; set; }

    /// <summary>
    /// Cron expression used for firing when this schedule is cron-based.
    /// Null when this schedule uses a fixed interval.
    /// </summary>
    public string? CronExpression { get; set; }

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

    public WorkflowDefinition? WorkflowDefinition { get; set; }
    public Tenant Tenant { get; set; } = null!;
}
