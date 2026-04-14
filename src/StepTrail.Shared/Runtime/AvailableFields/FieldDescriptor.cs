using System.Text.Json.Serialization;

namespace StepTrail.Shared.Runtime.AvailableFields;

/// <summary>
/// A single resolvable placeholder path and its associated metadata.
/// </summary>
public sealed class FieldDescriptor
{
    /// <summary>
    /// The full placeholder string, ready to embed in step config — e.g. <c>{{steps.fetch-order.output.statusCode}}</c>.
    /// </summary>
    [JsonPropertyName("placeholder")]
    public required string Placeholder { get; init; }

    /// <summary>
    /// JSON value kind this field resolves to: <c>string</c>, <c>number</c>, <c>boolean</c>, or <c>object</c>.
    /// Only <c>string</c>, <c>number</c>, and <c>boolean</c> fields are directly usable as scalar placeholders.
    /// </summary>
    [JsonPropertyName("fieldType")]
    public required string FieldType { get; init; }

    /// <summary>Optional human-readable note shown in a field picker UI.</summary>
    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}
