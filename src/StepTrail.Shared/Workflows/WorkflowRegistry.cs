namespace StepTrail.Shared.Workflows;

/// <summary>
/// In-memory registry populated at startup from all registered WorkflowDescriptor instances.
/// </summary>
public sealed class WorkflowRegistry : IWorkflowRegistry
{
    private readonly Dictionary<(string Key, int Version), WorkflowDescriptor> _index;

    public WorkflowRegistry(IEnumerable<WorkflowDescriptor> descriptors)
    {
        _index = new Dictionary<(string, int), WorkflowDescriptor>();

        foreach (var descriptor in descriptors)
        {
            Validate(descriptor);

            var key = (descriptor.Key, descriptor.Version);

            if (_index.ContainsKey(key))
                throw new InvalidOperationException(
                    $"Workflow '{descriptor.Key}' version {descriptor.Version} is registered more than once.");

            _index[key] = descriptor;
        }
    }

    public WorkflowDescriptor? Find(string key, int version) =>
        _index.TryGetValue((key, version), out var d) ? d : null;

    public WorkflowDescriptor? FindLatest(string key) =>
        _index.Values
            .Where(d => d.Key == key)
            .MaxBy(d => d.Version);

    public IReadOnlyList<WorkflowDescriptor> GetAll() =>
        _index.Values.OrderBy(d => d.Key).ThenBy(d => d.Version).ToList();

    private static void Validate(WorkflowDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.Key))
            throw new InvalidOperationException($"Workflow defined by '{descriptor.GetType().Name}' has an empty Key.");

        if (descriptor.Version < 1)
            throw new InvalidOperationException($"Workflow '{descriptor.Key}' has invalid version {descriptor.Version}. Version must be 1 or greater.");

        if (string.IsNullOrWhiteSpace(descriptor.Name))
            throw new InvalidOperationException($"Workflow '{descriptor.Key}' has an empty Name.");

        if (descriptor.Steps is null || descriptor.Steps.Count == 0)
            throw new InvalidOperationException($"Workflow '{descriptor.Key}' v{descriptor.Version} has no steps.");

        var duplicateKeys = descriptor.Steps
            .GroupBy(s => s.StepKey)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateKeys.Count > 0)
            throw new InvalidOperationException(
                $"Workflow '{descriptor.Key}' v{descriptor.Version} has duplicate step keys: {string.Join(", ", duplicateKeys)}.");

        var duplicateOrders = descriptor.Steps
            .GroupBy(s => s.Order)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateOrders.Count > 0)
            throw new InvalidOperationException(
                $"Workflow '{descriptor.Key}' v{descriptor.Version} has duplicate step orders: {string.Join(", ", duplicateOrders)}.");
    }
}
