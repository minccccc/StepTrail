using System.Text.Json;

namespace StepTrail.Shared.Workflows;

/// <summary>
/// Describes a single step within a workflow definition.
/// </summary>
public sealed class WorkflowStepDescriptor
{
    public WorkflowStepDescriptor(
        string stepKey,
        string stepType,
        int order,
        int maxAttempts = 3,
        int retryDelaySeconds = 30,
        int? timeoutSeconds = null,
        object? config = null)
    {
        if (string.IsNullOrWhiteSpace(stepKey))
            throw new ArgumentException("Step key must not be empty.", nameof(stepKey));
        if (string.IsNullOrWhiteSpace(stepType))
            throw new ArgumentException("Step type must not be empty.", nameof(stepType));
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order), "Step order must be 1 or greater.");
        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be at least 1.");
        if (retryDelaySeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(retryDelaySeconds), "RetryDelaySeconds must be 0 or greater.");
        if (timeoutSeconds is < 1)
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "TimeoutSeconds must be 1 or greater when specified.");

        StepKey = stepKey;
        StepType = stepType;
        Order = order;
        MaxAttempts = maxAttempts;
        RetryDelaySeconds = retryDelaySeconds;
        TimeoutSeconds = timeoutSeconds;
        Config = config is null ? null : JsonSerializer.Serialize(config);
    }

    /// <summary>
    /// Stable identifier for this step within the workflow. Example: "send-welcome-email".
    /// </summary>
    public string StepKey { get; }

    /// <summary>
    /// Name of the handler class responsible for executing this step.
    /// Example: "SendWelcomeEmailHandler".
    /// </summary>
    public string StepType { get; }

    /// <summary>
    /// 1-based execution order within the workflow.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Total number of attempts allowed before the step is permanently failed.
    /// Attempt 1 is the initial execution; attempts 2..N are retries.
    /// </summary>
    public int MaxAttempts { get; }

    /// <summary>
    /// Fixed delay in seconds between a failed attempt and the next retry.
    /// </summary>
    public int RetryDelaySeconds { get; }

    /// <summary>
    /// Maximum seconds a single attempt may run. Null = no handler-level timeout.
    /// </summary>
    public int? TimeoutSeconds { get; }

    /// <summary>
    /// Handler-specific configuration serialized as JSON.
    /// Passed to the handler at runtime via StepContext.Config.
    /// </summary>
    public string? Config { get; }
}
