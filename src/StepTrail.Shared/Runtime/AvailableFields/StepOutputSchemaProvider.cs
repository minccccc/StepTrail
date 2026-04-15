using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Runtime.AvailableFields;

/// <summary>
/// Returns the statically-known output fields for a given step definition.
///
/// Field lists reflect the stable output contract produced by each step type's handler:
///   HttpRequest - HttpRequestStepOutput shape
///   SendWebhook - SendWebhookStepOutput shape
///   Transform - one field per TransformValueMapping.TargetPath declared in the configuration
///   Conditional - ConditionalStepOutput shape
///   Delay - DelayStepOutput shape
///
/// Fields with fieldType = "object" are not directly resolvable as scalar placeholders;
/// they are included so a UI can inform the user of what exists without suggesting invalid usage.
/// </summary>
public static class StepOutputSchemaProvider
{
    public static IReadOnlyList<FieldDescriptor> GetOutputFields(StepDefinition step) =>
        step.Type switch
        {
            StepType.HttpRequest => HttpRequestOutputFields(step.Key),
            StepType.SendWebhook => SendWebhookOutputFields(step.Key),
            StepType.Transform when step.TransformConfiguration is not null
                                 => TransformOutputFields(step.Key, step.TransformConfiguration),
            StepType.Conditional => ConditionalOutputFields(step.Key),
            StepType.Delay => DelayOutputFields(step.Key),
            _ => []
        };

    private static IReadOnlyList<FieldDescriptor> HttpRequestOutputFields(string stepKey) =>
    [
        Field(stepKey, "statusCode", "number"),
        Field(
            stepKey,
            "body",
            "object",
            "Parsed response body. When the response content type is JSON, navigate nested fields at runtime " +
            $"(e.g. {{{{steps.{stepKey}.output.body.subscriptionId}}}}). For non-JSON responses the raw text is also available at bodyText."),
        Field(stepKey, "bodyText", "string"),
        Field(stepKey, "contentType", "string"),
        Field(
            stepKey,
            "headers",
            "object",
            "Response headers object. Not usable as a direct scalar placeholder - " +
            $"navigate to a specific header at runtime (e.g. {{{{steps.{stepKey}.output.headers.content-type}}}}).")
    ];

    private static IReadOnlyList<FieldDescriptor> SendWebhookOutputFields(string stepKey) =>
    [
        Field(stepKey, "delivered", "boolean"),
        Field(stepKey, "statusCode", "number"),
        Field(stepKey, "destination", "string"),
        Field(stepKey, "attemptedAtUtc", "string"),
        Field(stepKey, "responseBodyText", "string")
    ];

    private static IReadOnlyList<FieldDescriptor> TransformOutputFields(
        string stepKey,
        TransformStepConfiguration config) =>
        config.Mappings
              .Select(m => m.NormalizedTargetPath)
              .Distinct(StringComparer.Ordinal)
              .Select(path => Field(stepKey, path, "string"))
              .ToArray();

    private static IReadOnlyList<FieldDescriptor> ConditionalOutputFields(string stepKey) =>
    [
        Field(stepKey, "matched", "boolean"),
        Field(stepKey, "sourcePath", "string"),
        Field(stepKey, "operator", "string"),
        Field(stepKey, "actualValue", "string"),
        Field(stepKey, "expectedValue", "string"),
        Field(stepKey, "falseOutcome", "string")
    ];

    private static IReadOnlyList<FieldDescriptor> DelayOutputFields(string stepKey) =>
    [
        Field(stepKey, "delayType", "string"),
        Field(stepKey, "requestedDuration", "string"),
        Field(stepKey, "resumeAtUtc", "string"),
        Field(stepKey, "targetTimeUtc", "string"),
        Field(stepKey, "wasImmediate", "boolean")
    ];

    private static FieldDescriptor Field(
        string stepKey,
        string fieldPath,
        string type,
        string? note = null) =>
        new()
        {
            Placeholder = $"{{{{steps.{stepKey}.output.{fieldPath}}}}}",
            FieldType = type,
            Note = note
        };
}
