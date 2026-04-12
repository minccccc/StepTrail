namespace StepTrail.Shared.Workflows;

/// <summary>
/// Base class for code-first workflow definitions.
/// Inherit from this class to register a workflow with the system.
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
    /// Ordered list of steps. Must have at least one step.
    /// </summary>
    public abstract IReadOnlyList<WorkflowStepDescriptor> Steps { get; }
}
