using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Workflows;

/// <summary>
/// Base class for code-first workflow definitions (templates).
/// Inherit from this class to register a workflow blueprint with the system.
/// A template defines a complete workflow: trigger type + ordered steps.
/// </summary>
public abstract class WorkflowDescriptor
{
    /// <summary>
    /// Stable identifier used to reference this workflow type. Example: "user-onboarding".
    /// </summary>
    public abstract string Key { get; }

    /// <summary>
    /// Version of this workflow definition. Increment when making breaking changes.
    /// </summary>
    public abstract int Version { get; }

    /// <summary>
    /// Human-readable name. Example: "User Onboarding".
    /// </summary>
    public abstract string Name { get; }

    public virtual string? Description => null;

    /// <summary>
    /// The trigger type for this workflow template. Defaults to Webhook.
    /// </summary>
    public virtual TriggerType TriggerType => TriggerType.Webhook;

    /// <summary>
    /// When set, the recurring scheduler creates a new workflow instance every N seconds.
    /// Null (default) means the workflow is not recurring.
    /// </summary>
    public virtual int? RecurrenceIntervalSeconds => null;

    /// <summary>
    /// Ordered list of steps. Must have at least one step.
    /// </summary>
    public abstract IReadOnlyList<WorkflowStepDescriptor> Steps { get; }
}
