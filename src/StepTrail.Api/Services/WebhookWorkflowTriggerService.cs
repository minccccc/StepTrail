using System.Text.Json;
using StepTrail.Api.Models;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Trigger-specific start path for workflows configured with a Webhook trigger.
/// Resolves the active webhook endpoint, validates the inbound method, captures
/// raw request data, and then converges into the shared runtime instance creation flow.
/// </summary>
public sealed class WebhookWorkflowTriggerService
{
    private readonly WebhookIdempotencyKeyExtractor _webhookIdempotencyKeyExtractor;
    private readonly WebhookInputMapper _webhookInputMapper;
    private readonly WebhookSignatureValidationService _webhookSignatureValidationService;
    private readonly ExecutableWorkflowTriggerResolver _workflowTriggerResolver;
    private readonly WorkflowInstanceService _workflowInstanceService;

    public WebhookWorkflowTriggerService(
        WebhookIdempotencyKeyExtractor webhookIdempotencyKeyExtractor,
        WebhookInputMapper webhookInputMapper,
        WebhookSignatureValidationService webhookSignatureValidationService,
        ExecutableWorkflowTriggerResolver workflowTriggerResolver,
        WorkflowInstanceService workflowInstanceService)
    {
        _webhookIdempotencyKeyExtractor = webhookIdempotencyKeyExtractor;
        _webhookInputMapper = webhookInputMapper;
        _webhookSignatureValidationService = webhookSignatureValidationService;
        _workflowTriggerResolver = workflowTriggerResolver;
        _workflowInstanceService = workflowInstanceService;
    }

    public async Task<(StartWorkflowResponse Response, bool Created)> StartAsync(
        StartWebhookWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RouteKey))
            throw new ArgumentException("Webhook route key must not be empty.", nameof(request));
        if (request.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.HttpMethod))
            throw new ArgumentException("Webhook HTTP method must not be empty.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RawBody))
            throw new WebhookTriggerPayloadInvalidException("Webhook request body is required.");
        if (request.Payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new WebhookTriggerPayloadInvalidException("Webhook request body must contain a valid JSON payload.");

        var definition = await _workflowTriggerResolver.ResolveActiveWebhookAsync(
            request.RouteKey,
            cancellationToken);

        var webhookConfiguration = definition.TriggerDefinition.Type == TriggerType.Webhook
            ? definition.TriggerDefinition.WebhookConfiguration
            : null;

        if (webhookConfiguration is null || string.IsNullOrWhiteSpace(webhookConfiguration.RouteKey))
        {
            throw new WorkflowTriggerMismatchException(
                $"Workflow definition '{definition.Key}' v{definition.Version} does not support webhook trigger starts.");
        }

        if (!string.Equals(webhookConfiguration.HttpMethod, request.HttpMethod.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new WebhookTriggerMethodNotAllowedException(
                $"Webhook route '{webhookConfiguration.RouteKey}' requires HTTP {webhookConfiguration.HttpMethod}.");
        }

        if (webhookConfiguration.SignatureValidation is not null)
        {
            await _webhookSignatureValidationService.ValidateAsync(
                webhookConfiguration.SignatureValidation,
                request.RawBody,
                request.Headers,
                cancellationToken);
        }

        var mappedInput = _webhookInputMapper.MapOrPassThrough(
            request.Payload,
            request.Headers,
            request.Query,
            webhookConfiguration.InputMappings);
        var idempotencyKey = _webhookIdempotencyKeyExtractor.ExtractOrNone(
            request.Payload,
            request.Headers,
            webhookConfiguration.IdempotencyKeyExtraction);

        var triggerData = BuildTriggerData(
            webhookConfiguration.RouteKey,
            webhookConfiguration.HttpMethod,
            request.RawBody,
            request.Payload,
            request.Headers,
            request.Query);

        return await _workflowInstanceService.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowKey = definition.Key,
                Version = definition.Version,
                TenantId = request.TenantId,
                ExternalKey = request.ExternalKey,
                IdempotencyKey = idempotencyKey,
                Input = mappedInput,
                TriggerData = triggerData
            },
            cancellationToken);
    }

    private static string BuildTriggerData(
        string routeKey,
        string httpMethod,
        string rawBody,
        JsonElement payload,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? query) =>
        JsonSerializer.Serialize(new
        {
            source = "webhook",
            routeKey,
            httpMethod,
            receivedAtUtc = DateTimeOffset.UtcNow,
            bodyRaw = rawBody,
            body = payload,
            headers = headers ?? new Dictionary<string, string>(),
            query = query ?? new Dictionary<string, string>()
        });
}

public sealed class WebhookTriggerMethodNotAllowedException : Exception
{
    public WebhookTriggerMethodNotAllowedException(string message) : base(message) { }
}

public sealed class WebhookTriggerPayloadInvalidException : Exception
{
    public WebhookTriggerPayloadInvalidException(string message) : base(message) { }
}
