namespace StepTrail.Shared.Definitions;

/// <summary>
/// Internal executable workflow definition contract used as the aggregate root for
/// trigger and ordered step definitions. This model intentionally excludes UI concerns.
/// </summary>
public sealed class WorkflowDefinition
{
    private readonly List<StepDefinition> _stepDefinitions = [];

    private WorkflowDefinition()
    {
        Key = string.Empty;
        Name = string.Empty;
        TriggerDefinition = null!;
    }

    public WorkflowDefinition(
        Guid id,
        string key,
        string name,
        int version,
        WorkflowDefinitionStatus status,
        TriggerDefinition triggerDefinition,
        IEnumerable<StepDefinition> stepDefinitions,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        string? description = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Workflow definition id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Workflow definition key must not be empty.", nameof(key));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Workflow definition name must not be empty.", nameof(name));
        if (version < 1)
            throw new ArgumentOutOfRangeException(nameof(version), "Workflow definition version must be 1 or greater.");
        ArgumentNullException.ThrowIfNull(triggerDefinition);
        ArgumentNullException.ThrowIfNull(stepDefinitions);

        var orderedStepDefinitions = stepDefinitions
            .OrderBy(step => step.Order)
            .ToList();

        if (orderedStepDefinitions.Count == 0)
            throw new ArgumentException("Workflow definition must contain at least one step definition.", nameof(stepDefinitions));
        if (updatedAtUtc < createdAtUtc)
            throw new ArgumentOutOfRangeException(nameof(updatedAtUtc), "UpdatedAtUtc must be greater than or equal to CreatedAtUtc.");

        var duplicateStepKeys = orderedStepDefinitions
            .GroupBy(step => step.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(keyValue => keyValue, StringComparer.Ordinal)
            .ToList();

        if (duplicateStepKeys.Count > 0)
            throw new ArgumentException(
                $"Workflow definition contains duplicate step keys: {string.Join(", ", duplicateStepKeys)}.",
                nameof(stepDefinitions));

        var duplicateStepOrders = orderedStepDefinitions
            .GroupBy(step => step.Order)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(orderValue => orderValue)
            .ToList();

        if (duplicateStepOrders.Count > 0)
            throw new ArgumentException(
                $"Workflow definition contains duplicate step orders: {string.Join(", ", duplicateStepOrders)}.",
                nameof(stepDefinitions));

        Id = id;
        Key = key.Trim();
        Name = name.Trim();
        Version = version;
        Status = status;
        TriggerDefinition = triggerDefinition;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        _stepDefinitions.AddRange(orderedStepDefinitions);
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; }
    public string Name { get; private set; }
    public int Version { get; private set; }
    public WorkflowDefinitionStatus Status { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public TriggerDefinition TriggerDefinition { get; private set; }
    public IReadOnlyList<StepDefinition> StepDefinitions => _stepDefinitions;
}
