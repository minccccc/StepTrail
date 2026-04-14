using StepTrail.Shared.Runtime;
using StepTrail.Shared.Runtime.Placeholders;

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
    private static readonly PlaceholderResolver Resolver = new();
    public required Guid WorkflowInstanceId { get; init; }
    public required Guid StepExecutionId { get; init; }
    public required string StepKey { get; init; }
    public string? Input { get; init; }

    /// <summary>
    /// Handler-specific configuration JSON, sourced from WorkflowDefinitionStep.Config.
    /// Each handler type defines and deserializes its own config shape.
    /// </summary>
    public string? Config { get; init; }

    /// <summary>
    /// Full workflow runtime state at the moment this step is about to execute.
    /// Used by handlers to resolve {{input.*}} and {{steps.*.output.*}} placeholders
    /// in config strings. Populated by StepExecutionProcessor before invoking the handler.
    /// </summary>
    public WorkflowState? State { get; init; }

    /// <summary>
    /// All registered secrets, keyed by name. Pre-loaded from the store so handlers
    /// can resolve {{secrets.*}} placeholders synchronously via PlaceholderResolver.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Secrets { get; init; }

    /// <summary>
    /// Resolves all {{...}} placeholders in <paramref name="template"/> against the current state and secrets.
    /// Returns an empty string for null/empty input. Throws InvalidOperationException when any
    /// placeholder cannot be resolved so the step fails with a descriptive message.
    /// </summary>
    public string Resolve(string? template, string fieldDescription)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;
        if (State is null) return template;

        var result = Resolver.Resolve(
            template, State, Secrets ?? new Dictionary<string, string>());

        if (!result.IsSuccess)
            throw new InvalidOperationException(
                $"Step '{StepKey}': {fieldDescription} placeholder resolution failed — {result.Error}");

        return result.Value!;
    }
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
