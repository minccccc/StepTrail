namespace StepTrail.Api.Models;
using System.Text.Json;

public sealed class StartWebhookWorkflowRequest
{
    /// <summary>
    /// Stable persisted webhook route key used to identify the active workflow endpoint.
    /// </summary>
    public string RouteKey { get; set; } = string.Empty;

    /// <summary>
    /// Tenant for which the workflow instance should be created.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Optional correlation identifier supplied by the caller.
    /// </summary>
    public string? ExternalKey { get; set; }

    /// <summary>
    /// HTTP method used by the inbound webhook request.
    /// </summary>
    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>
    /// Raw request body exactly as received.
    /// </summary>
    public string RawBody { get; set; } = string.Empty;

    /// <summary>
    /// Parsed JSON body used as the initial normalized workflow input for the first webhook version.
    /// </summary>
    public JsonElement Payload { get; set; }

    /// <summary>
    /// Non-sensitive request headers captured for debugging and raw trigger data.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; set; }

    /// <summary>
    /// Request query values captured for debugging and raw trigger data.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Query { get; set; }
}
