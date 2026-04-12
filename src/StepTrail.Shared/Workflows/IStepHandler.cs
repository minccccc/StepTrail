namespace StepTrail.Shared.Workflows;

/// <summary>
/// Implemented by every step handler that the worker can execute.
/// Register implementations with keyed DI using the StepType name as the key.
/// </summary>
public interface IStepHandler
{
    Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct);
}

/// <summary>
/// Input passed to a step handler when the worker runs it.
/// </summary>
public sealed class StepContext
{
    public required Guid WorkflowInstanceId { get; init; }
    public required Guid StepExecutionId { get; init; }
    public required string StepKey { get; init; }
    public string? Input { get; init; }
}

/// <summary>
/// Outcome returned by a step handler on successful completion.
/// </summary>
public sealed class StepResult
{
    /// <summary>
    /// Optional JSON output produced by the handler, stored on the step execution.
    /// </summary>
    public string? Output { get; init; }

    public static StepResult Success(string? output = null) => new() { Output = output };
}
