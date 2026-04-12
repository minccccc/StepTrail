namespace StepTrail.Shared.Entities;

public class WorkflowDefinitionStep
{
    public Guid Id { get; set; }
    public Guid WorkflowDefinitionId { get; set; }

    /// <summary>
    /// Stable key identifying the step within the workflow.
    /// Example: "send-welcome-email", "provision-account"
    /// </summary>
    public string StepKey { get; set; } = string.Empty;

    /// <summary>
    /// The handler type name used to resolve the step executor.
    /// Example: "SendWelcomeEmailStepHandler"
    /// </summary>
    public string StepType { get; set; } = string.Empty;

    /// <summary>
    /// 1-based execution order within the workflow.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Total number of attempts allowed before the step is permanently failed.
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>
    /// Fixed delay in seconds between a failed attempt and the next retry.
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 30;

    public DateTimeOffset CreatedAt { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public ICollection<WorkflowStepExecution> Executions { get; set; } = [];
}
