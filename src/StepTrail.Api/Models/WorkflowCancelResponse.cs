namespace StepTrail.Api.Models;

public sealed class WorkflowCancelResponse
{
    public Guid InstanceId { get; init; }
    public string InstanceStatus { get; init; } = string.Empty;
}
