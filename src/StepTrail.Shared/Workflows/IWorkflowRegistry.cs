namespace StepTrail.Shared.Workflows;

public interface IWorkflowRegistry
{
    /// <summary>
    /// Returns the descriptor for a specific workflow key and version, or null if not found.
    /// </summary>
    WorkflowDescriptor? Find(string key, int version);

    /// <summary>
    /// Returns the highest-version descriptor registered for the given key, or null if not found.
    /// </summary>
    WorkflowDescriptor? FindLatest(string key);

    /// <summary>
    /// Returns all registered workflow descriptors.
    /// </summary>
    IReadOnlyList<WorkflowDescriptor> GetAll();
}
