namespace StepTrail.Api.Models;

public sealed class StartWorkflowRequest
{
    /// <summary>
    /// The workflow key to start. Example: "user-onboarding".
    /// </summary>
    public string WorkflowKey { get; set; } = string.Empty;

    /// <summary>
    /// Specific version to start. If omitted, the latest registered version is used.
    /// </summary>
    public int? Version { get; set; }

    /// <summary>
    /// The tenant on whose behalf this workflow is being started.
    /// For local development use: 00000000-0000-0000-0000-000000000001
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Caller-provided correlation identifier. Optional.
    /// Example: the user ID or order ID this workflow relates to.
    /// </summary>
    public string? ExternalKey { get; set; }

    /// <summary>
    /// Idempotency key. If a workflow instance was already started with this key
    /// for the same tenant, the existing instance is returned instead of creating a new one.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Arbitrary JSON payload passed as input to the workflow.
    /// </summary>
    public object? Input { get; set; }
}
