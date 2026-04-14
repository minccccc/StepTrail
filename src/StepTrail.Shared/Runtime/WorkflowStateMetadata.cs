using StepTrail.Shared.Entities;

namespace StepTrail.Shared.Runtime;

/// <summary>
/// Workflow-level execution metadata extracted from the workflow instance row.
/// Part of the canonical WorkflowState tree.
/// </summary>
public sealed class WorkflowStateMetadata
{
    public WorkflowStateMetadata(
        Guid instanceId,
        string definitionKey,
        int definitionVersion,
        WorkflowInstanceStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt)
    {
        InstanceId = instanceId;
        DefinitionKey = definitionKey;
        DefinitionVersion = definitionVersion;
        Status = status;
        CreatedAt = createdAt;
        CompletedAt = completedAt;
    }

    public Guid InstanceId { get; }
    public string DefinitionKey { get; }
    public int DefinitionVersion { get; }
    public WorkflowInstanceStatus Status { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? CompletedAt { get; }
}
