namespace StepTrail.TestLab;

public sealed class LabSnapshot
{
    public required string ActiveScenario { get; init; }
    public required int ApiACallCount { get; init; }
    public required int ApiBCallCount { get; init; }
    public required bool DemoWorkflowReady { get; init; }
    public string? DemoWorkflowStatus { get; init; }
    public Guid? LastWorkflowInstanceId { get; init; }
    public string? LastTriggerSummary { get; init; }
    public required IReadOnlyList<LabRequestRecord> Requests { get; init; }
}
