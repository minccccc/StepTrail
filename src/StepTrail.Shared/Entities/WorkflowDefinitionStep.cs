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

    /// <summary>
    /// Maximum seconds a single attempt may run before it is cancelled with a timeout error.
    /// Null means no explicit handler timeout — the lock expiry window still applies.
    /// </summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>
    /// Executor-specific configuration stored as JSON.
    /// Passed to the step executor via StepExecutionRequest.StepConfiguration so each
    /// executor type can define its own shape. Example: HttpActivityHandler reads Url,
    /// Method, Headers, and Body from this field.
    /// </summary>
    public string? Config { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public WorkflowDefinition WorkflowDefinition { get; set; } = null!;
    public ICollection<WorkflowStepExecution> Executions { get; set; } = [];
}
