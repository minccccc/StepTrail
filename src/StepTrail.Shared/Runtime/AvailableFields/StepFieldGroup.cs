using System.Text.Json.Serialization;

namespace StepTrail.Shared.Runtime.AvailableFields;

/// <summary>
/// All placeholder fields contributed by a single prior step's output.
/// Empty <see cref="Fields"/> means the step type produces no output.
/// </summary>
public sealed class StepFieldGroup
{
    [JsonPropertyName("stepKey")]
    public required string StepKey { get; init; }

    [JsonPropertyName("stepType")]
    public required string StepType { get; init; }

    [JsonPropertyName("fields")]
    public required IReadOnlyList<FieldDescriptor> Fields { get; init; }
}
