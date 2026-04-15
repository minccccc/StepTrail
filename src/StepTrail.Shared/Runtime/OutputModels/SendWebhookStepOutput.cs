using System.Text.Json.Serialization;

namespace StepTrail.Shared.Runtime.OutputModels;

public sealed class SendWebhookStepOutput
{
    [JsonPropertyName("delivered")]
    public required bool Delivered { get; init; }

    [JsonPropertyName("statusCode")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? StatusCode { get; init; }

    [JsonPropertyName("destination")]
    public required string Destination { get; init; }

    [JsonPropertyName("attemptedAtUtc")]
    public required DateTimeOffset AttemptedAtUtc { get; init; }

    [JsonPropertyName("responseBodyText")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponseBodyText { get; init; }
}
