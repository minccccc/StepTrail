namespace StepTrail.Api.Models;

public sealed class WorkflowTimelineEvent
{
    public Guid Id { get; init; }
    public string EventType { get; init; } = string.Empty;

    /// <summary>Step key this event relates to, if any.</summary>
    public string? StepKey { get; init; }

    /// <summary>Attempt number of the step execution this event relates to, if any.</summary>
    public int? StepAttempt { get; init; }

    public string? Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}
