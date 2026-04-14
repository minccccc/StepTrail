using System.Text.Json.Serialization;

namespace StepTrail.Shared.Runtime.AvailableFields;

/// <summary>
/// All placeholder paths available when configuring a specific step within a workflow.
///
/// Three source categories:
///   <list type="bullet">
///     <item><b>input</b> — <c>{{input.*}}</c> paths from the workflow's start payload (schema-less; see <see cref="InputNote"/>).</item>
///     <item><b>steps</b> — <c>{{steps.&lt;name&gt;.output.*}}</c> paths from each prior step's stable output contract.</item>
///     <item><b>secrets</b> — <c>{{secrets.&lt;name&gt;}}</c> references for every secret registered in the tenant.</item>
///   </list>
/// </summary>
public sealed class AvailableFieldsResponse
{
    /// <summary>
    /// Guidance about <c>{{input.*}}</c> usage.
    /// Field names are not enumerable at design time because the workflow has no declared input schema.
    /// </summary>
    [JsonPropertyName("inputNote")]
    public required string InputNote { get; init; }

    /// <summary>Fields contributed by each prior step, in execution order.</summary>
    [JsonPropertyName("steps")]
    public required IReadOnlyList<StepFieldGroup> Steps { get; init; }

    /// <summary>All available secret references.</summary>
    [JsonPropertyName("secrets")]
    public required IReadOnlyList<FieldDescriptor> Secrets { get; init; }
}
