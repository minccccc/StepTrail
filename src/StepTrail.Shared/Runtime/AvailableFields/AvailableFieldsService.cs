using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Runtime.AvailableFields;

/// <summary>
/// Builds the set of placeholder paths available when configuring a specific step within a workflow.
///
/// This is a pure, stateless operation with no database dependency.
/// The caller is responsible for supplying the loaded workflow definition and secret names.
/// </summary>
public static class AvailableFieldsService
{
    private const string InputNote =
        "Use {{input.fieldName}} to reference any field from the workflow input payload. " +
        "Paths are resolved at runtime — the specific fields depend on the JSON body " +
        "provided when the workflow instance was started.";

    /// <summary>
    /// Returns all placeholder paths available for the given target step.
    /// </summary>
    /// <param name="definition">The workflow definition containing the target step.</param>
    /// <param name="targetStepKey">Key of the step being configured. Only prior steps contribute output fields.</param>
    /// <param name="secretNames">Names of all registered secrets; each becomes a <c>{{secrets.*}}</c> reference.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="targetStepKey"/> is not found in the definition.</exception>
    public static AvailableFieldsResponse GetAvailableFields(
        WorkflowDefinition definition,
        string targetStepKey,
        IReadOnlyList<string> secretNames)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(secretNames);

        if (string.IsNullOrWhiteSpace(targetStepKey))
            throw new ArgumentException("Target step key must not be empty.", nameof(targetStepKey));

        var targetStep = definition.StepDefinitions
            .FirstOrDefault(s => string.Equals(s.Key, targetStepKey, StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"Step '{targetStepKey}' not found in workflow '{definition.Key}'.",
                nameof(targetStepKey));

        // Only steps that execute before the target step contribute available output fields.
        var stepGroups = definition.StepDefinitions
            .Where(s => s.Order < targetStep.Order)
            .OrderBy(s => s.Order)
            .Select(s => new StepFieldGroup
            {
                StepKey  = s.Key,
                StepType = s.Type.ToString(),
                Fields   = StepOutputSchemaProvider.GetOutputFields(s)
            })
            .ToList();

        var secrets = secretNames
            .Order(StringComparer.Ordinal)
            .Select(name => new FieldDescriptor
            {
                Placeholder = $"{{{{secrets.{name}}}}}",
                FieldType   = "string"
            })
            .ToList<FieldDescriptor>();

        return new AvailableFieldsResponse
        {
            InputNote = InputNote,
            Steps     = stepGroups,
            Secrets   = secrets
        };
    }
}
