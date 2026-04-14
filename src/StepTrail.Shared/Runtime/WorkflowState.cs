namespace StepTrail.Shared.Runtime;

/// <summary>
/// Canonical runtime state of a workflow instance.
///
/// Assembled from relational rows, this is the single coherent state tree used by:
/// - placeholder resolution ({{input.*}}, {{steps.*.*}}, {{secrets.*}})
/// - trail/timeline view
/// - debugging and diagnostics
///
/// Logical shape:
/// {
///   "trigger_data": { "body": {}, "headers": {}, "query": {} },
///   "input":        { ...normalized workflow input... },
///   "steps": {
///     "step_name": {
///       "status":   "completed",
///       "output":   {},
///       "error":    null,
///       "attempts": [...]
///     }
///   },
///   "metadata": { "definitionKey": "...", "definitionVersion": 1 }
/// }
/// </summary>
public sealed class WorkflowState
{
    public WorkflowState(
        WorkflowStateMetadata metadata,
        string? triggerData,
        string? input,
        IReadOnlyDictionary<string, WorkflowStepState> steps)
    {
        Metadata = metadata;
        TriggerData = triggerData;
        Input = input;
        Steps = steps;
    }

    /// <summary>Workflow-level metadata: definition key/version, status, timestamps.</summary>
    public WorkflowStateMetadata Metadata { get; }

    /// <summary>
    /// Raw inbound trigger payload, preserved exactly as received.
    /// For webhooks: { "body": {...}, "headers": {...}, "query": {...} }.
    /// Populated by PBI-0302; null until the trigger_data column is introduced.
    /// Step executors must not mutate this value.
    /// </summary>
    public string? TriggerData { get; }

    /// <summary>
    /// Normalized workflow input. This is the stable value that downstream steps
    /// and {{input.*}} placeholders resolve against.
    /// Derived from trigger data via mapping rules (basic pass-through until mapping
    /// is introduced in a later PBI).
    /// </summary>
    public string? Input { get; }

    /// <summary>
    /// Per-step state keyed by step key.
    /// Only includes steps that have at least one execution row.
    /// </summary>
    public IReadOnlyDictionary<string, WorkflowStepState> Steps { get; }
}
