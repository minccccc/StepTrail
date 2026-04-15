using System.Text.Json.Serialization;

namespace StepTrail.Shared.Runtime.OutputModels;

public sealed class DelayStepOutput
{
    [JsonPropertyName("delayType")]
    public required string DelayType { get; init; }

    [JsonPropertyName("requestedDuration")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RequestedDuration { get; init; }

    [JsonPropertyName("resumeAtUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ResumeAtUtc { get; init; }

    [JsonPropertyName("targetTimeUtc")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? TargetTimeUtc { get; init; }

    [JsonPropertyName("wasImmediate")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool WasImmediate { get; init; }
}
