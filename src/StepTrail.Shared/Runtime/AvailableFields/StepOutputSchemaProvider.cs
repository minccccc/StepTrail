using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Runtime.AvailableFields;

/// <summary>
/// Returns the statically-known output fields for a given step definition.
///
/// Field lists reflect the stable output contract produced by each step type's handler:
///   HttpRequest / SendWebhook — <see cref="OutputModels.HttpRequestStepOutput"/> shape
///   Transform               — one field per <see cref="TransformValueMapping.TargetPath"/> declared in the configuration
///   Conditional / Delay     — no output fields
///
/// Fields with <c>fieldType = "object"</c> are not directly resolvable as scalar placeholders;
/// they are included so a UI can inform the user of what exists without suggesting invalid usage.
/// </summary>
public static class StepOutputSchemaProvider
{
    public static IReadOnlyList<FieldDescriptor> GetOutputFields(StepDefinition step) =>
        step.Type switch
        {
            StepType.HttpRequest or StepType.SendWebhook => HttpRequestOutputFields(step.Key),
            StepType.Transform when step.TransformConfiguration is not null
                                 => TransformOutputFields(step.Key, step.TransformConfiguration),
            _ => []
        };

    // ── Per-type schemas ──────────────────────────────────────────────────────

    private static IReadOnlyList<FieldDescriptor> HttpRequestOutputFields(string stepKey) =>
    [
        Field(stepKey, "statusCode", "number"),
        Field(stepKey, "body",       "string"),
        Field(stepKey, "headers",    "object",
            "Response headers object. Not usable as a direct scalar placeholder — " +
            "navigate to a specific header at runtime (e.g. {{steps." + stepKey + ".output.headers.content-type}}).")
    ];

    private static IReadOnlyList<FieldDescriptor> TransformOutputFields(
        string stepKey,
        TransformStepConfiguration config) =>
        config.Mappings
              .Select(m => Field(stepKey, m.TargetPath, "string"))
              .ToArray();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static FieldDescriptor Field(
        string stepKey,
        string fieldPath,
        string type,
        string? note = null) =>
        new()
        {
            Placeholder = $"{{{{steps.{stepKey}.output.{fieldPath}}}}}",
            FieldType   = type,
            Note        = note
        };
}
