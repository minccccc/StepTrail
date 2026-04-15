namespace StepTrail.Api.Models;

public sealed class StartManualWorkflowRequest
{
    /// <summary>
    /// The workflow key to start manually.
    /// </summary>
    public string WorkflowKey { get; set; } = string.Empty;

    /// <summary>
    /// Specific workflow definition version to start.
    /// If omitted, the current active version is used.
    /// </summary>
    public int? Version { get; set; }

    /// <summary>
    /// The tenant on whose behalf the workflow is being started.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Optional caller-provided correlation identifier.
    /// </summary>
    public string? ExternalKey { get; set; }

    /// <summary>
    /// Optional idempotency key for duplicate manual submissions.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Raw payload submitted by the operator or internal caller.
    /// The first version of the manual trigger uses this payload as both trigger_data payload
    /// and normalized workflow input.
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>
    /// Optional operator or actor identifier associated with the manual start.
    /// When omitted, the authenticated principal name may be used by the endpoint layer.
    /// </summary>
    public string? ActorId { get; set; }
}
