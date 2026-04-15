using StepTrail.Shared.Runtime;
using StepTrail.Shared.Runtime.Placeholders;
using System.Text.Json;

namespace StepTrail.Shared.Workflows;

/// <summary>
/// Shared execution request passed from the runtime engine to a step executor.
/// Carries the persisted workflow context plus helper methods for resolving
/// placeholders against the canonical workflow state.
/// </summary>
public sealed class StepExecutionRequest
{
    private static readonly PlaceholderResolver Resolver = new();

    public required Guid WorkflowInstanceId { get; init; }
    public required Guid StepExecutionId { get; init; }
    public required string WorkflowDefinitionKey { get; init; }
    public int? WorkflowDefinitionVersion { get; init; }
    public required string StepKey { get; init; }
    public string? StepType { get; init; }
    public string? Input { get; init; }
    public string? CurrentOutput { get; init; }

    /// <summary>
    /// Raw type-specific configuration JSON for the step executor.
    /// Executors own deserializing and validating their own configuration shape.
    /// </summary>
    public string? StepConfiguration { get; init; }

    /// <summary>
    /// Canonical runtime state assembled from the workflow instance and prior step executions.
    /// </summary>
    public WorkflowState? State { get; init; }

    /// <summary>
    /// All currently available secrets keyed by name.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Secrets { get; init; }

    /// <summary>
    /// Resolves all {{...}} placeholders in <paramref name="template"/> against the current
    /// workflow state and secrets. When state is not present, the template is returned unchanged
    /// to preserve current legacy behavior in non-stateful call sites.
    /// </summary>
    public ResolveResult ResolveTemplate(string? template, string fieldDescription)
    {
        if (string.IsNullOrEmpty(template))
            return ResolveResult.Success(string.Empty);

        if (State is null)
            return ResolveResult.Success(template);

        var result = Resolver.Resolve(
            template,
            State,
            Secrets ?? EmptySecrets.Instance);

        if (!result.IsSuccess)
        {
            return ResolveResult.Failure(
                $"Step '{StepKey}': {fieldDescription} placeholder resolution failed - {result.Error}");
        }

        return result;
    }

    /// <summary>
    /// Resolves a single typed value reference against workflow state.
    /// Supports:
    /// - a single placeholder reference such as {{input.customer.id}} or {{steps.fetch.output.body}}
    /// - legacy input-root aliases such as $.customer.id
    /// </summary>
    public ValueResolutionResult ResolveValueReference(string? reference, string fieldDescription)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return ValueResolutionResult.InvalidConfiguration(
                $"Step '{StepKey}': {fieldDescription} must not be empty.");
        }

        if (State is null)
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} value resolution requires workflow state, but no state was supplied.");
        }

        var normalizedReference = reference.Trim();

        if (IsLegacyInputPath(normalizedReference))
            return ResolveLegacyInputReference(normalizedReference, fieldDescription);

        var parseResult = PlaceholderParser.Parse(normalizedReference);
        if (!parseResult.IsSuccess)
        {
            return ValueResolutionResult.InvalidConfiguration(
                $"Step '{StepKey}': {fieldDescription} is invalid - {parseResult.Error}");
        }

        if (parseResult.Segments.Count != 1 || parseResult.Segments[0] is not PlaceholderSegment placeholder)
        {
            return ValueResolutionResult.InvalidConfiguration(
                $"Step '{StepKey}': {fieldDescription} must be a single placeholder reference with no surrounding literal text.");
        }

        return ResolvePlaceholderValue(placeholder, fieldDescription);
    }

    private sealed class EmptySecrets : Dictionary<string, string>
    {
        public static readonly EmptySecrets Instance = new();
    }

    private ValueResolutionResult ResolveLegacyInputReference(string reference, string fieldDescription)
    {
        var path = reference == "$"
            ? []
            : reference[2..]
                .Split('.', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        var display = reference == "$" ? "$" : reference.Trim();

        if (State!.Input is null)
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} could not be resolved from '{display}' because workflow input is null.");
        }

        return NavigateJson(State.Input, path, fieldDescription, display);
    }

    private ValueResolutionResult ResolvePlaceholderValue(PlaceholderSegment placeholder, string fieldDescription) =>
        placeholder.Root switch
        {
            PlaceholderRoot.Input => ResolveInputValue(placeholder, fieldDescription),
            PlaceholderRoot.Steps => ResolveStepValue(placeholder, fieldDescription),
            PlaceholderRoot.Secrets => ResolveSecretValue(placeholder, fieldDescription),
            _ => ValueResolutionResult.InvalidConfiguration(
                $"Step '{StepKey}': {fieldDescription} uses an unsupported placeholder root '{placeholder.Root}'.")
        };

    private ValueResolutionResult ResolveInputValue(PlaceholderSegment placeholder, string fieldDescription)
    {
        var display = "{{input." + string.Join(".", placeholder.Path) + "}}";

        if (State!.Input is null)
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because workflow input is null.");
        }

        return NavigateJson(State.Input, placeholder.Path, fieldDescription, display);
    }

    private ValueResolutionResult ResolveStepValue(PlaceholderSegment placeholder, string fieldDescription)
    {
        var display = "{{steps." + string.Join(".", placeholder.Path) + "}}";
        var stepName = placeholder.StepName;

        if (!State!.Steps.TryGetValue(stepName, out var stepState))
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because step '{stepName}' has no execution data.");
        }

        var navigationPath = placeholder.NavigationPath;
        var accessor = navigationPath[0];
        if (!string.Equals(accessor, "output", StringComparison.Ordinal))
        {
            return ValueResolutionResult.InvalidConfiguration(
                $"Step '{StepKey}': {fieldDescription} uses unsupported step accessor '{accessor}'. Only 'output' is supported.");
        }

        if (stepState.Output is null)
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because step '{stepName}' has no output.");
        }

        return NavigateJson(stepState.Output, navigationPath[1..], fieldDescription, display);
    }

    private ValueResolutionResult ResolveSecretValue(PlaceholderSegment placeholder, string fieldDescription)
    {
        var secretName = placeholder.Path[0];
        var display = "{{secrets." + secretName + "}}";

        if (!(Secrets ?? EmptySecrets.Instance).TryGetValue(secretName, out var secretValue))
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because secret '{secretName}' was not found.");
        }

        return ValueResolutionResult.Success(JsonSerializer.SerializeToElement(secretValue));
    }

    private ValueResolutionResult NavigateJson(
        string json,
        string[] path,
        string fieldDescription,
        string display)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ValueResolutionResult.InputResolutionFailure(
                $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because the source JSON could not be parsed - {ex.Message}");
        }

        using (document)
        {
            var current = document.RootElement;

            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object)
                {
                    return ValueResolutionResult.InputResolutionFailure(
                        $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because '{segment}' was accessed on a {current.ValueKind} value.");
                }

                if (!current.TryGetProperty(segment, out current))
                {
                    return ValueResolutionResult.InputResolutionFailure(
                        $"Step '{StepKey}': {fieldDescription} could not resolve '{display}' because field '{segment}' was not found.");
                }
            }

            return ValueResolutionResult.Success(current);
        }
    }

    private static bool IsLegacyInputPath(string reference) =>
        reference == "$" || reference.StartsWith("$.", StringComparison.Ordinal);
}
