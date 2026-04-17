namespace StepTrail.Api.Models;

public sealed class WorkflowRetryResponse
{
    public Guid InstanceId { get; init; }
    public string WorkflowKey { get; init; } = string.Empty;
    public string InstanceStatus { get; init; } = string.Empty;

    /// <summary>
    /// The newly created step execution that the worker will pick up next.
    /// </summary>
    public Guid NewStepExecutionId { get; init; }
    public string StepKey { get; init; } = string.Empty;
}
