namespace StepTrail.Api.Models;

public sealed class StartApiWorkflowRequest
{
    /// <summary>
    /// The workflow key to start through an API trigger.
    /// </summary>
    public string WorkflowKey { get; set; } = string.Empty;

    /// <summary>
    /// Specific executable workflow definition version to start.
    /// If omitted, the active version is used.
    /// </summary>
    public int? Version { get; set; }

    /// <summary>
    /// Tenant for which the workflow instance should be created.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Optional correlation identifier supplied by the caller.
    /// </summary>
    public string? ExternalKey { get; set; }

    /// <summary>
    /// Optional idempotency key supplied by the caller.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Presented shared secret for API trigger authentication.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Request payload submitted to the API trigger.
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>
    /// Non-sensitive request headers captured for debugging and raw trigger data.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Request query values captured for debugging and raw trigger data.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Query { get; set; }
}
