using System.Text.Json;
using StepTrail.Api.Models;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Trigger-specific start path for workflows configured with an API trigger.
/// Validates the workflow trigger contract, captures raw API trigger data, and then
/// converges into the shared runtime instance creation flow.
/// </summary>
public sealed class ApiWorkflowTriggerService
{
    private readonly ApiTriggerAuthenticationService _apiTriggerAuthenticationService;
    private readonly ExecutableWorkflowTriggerResolver _workflowTriggerResolver;
    private readonly WorkflowInstanceService _workflowInstanceService;

    public ApiWorkflowTriggerService(
        ApiTriggerAuthenticationService apiTriggerAuthenticationService,
        ExecutableWorkflowTriggerResolver workflowTriggerResolver,
        WorkflowInstanceService workflowInstanceService)
    {
        _apiTriggerAuthenticationService = apiTriggerAuthenticationService;
        _workflowTriggerResolver = workflowTriggerResolver;
        _workflowInstanceService = workflowInstanceService;
    }

    public async Task<(StartWorkflowResponse Response, bool Created)> StartAsync(
        StartApiWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkflowKey))
            throw new ArgumentException("Workflow key must not be empty.", nameof(request));
        if (request.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(request));

        _apiTriggerAuthenticationService.EnsureAuthenticated(request.ApiKey);

        var definition = await _workflowTriggerResolver.ResolveActiveAsync(
            request.WorkflowKey,
            request.Version,
            cancellationToken);

        var apiConfiguration = definition.TriggerDefinition.Type == TriggerType.Api
            ? definition.TriggerDefinition.ApiConfiguration
            : null;

        if (apiConfiguration is null || string.IsNullOrWhiteSpace(apiConfiguration.OperationKey))
        {
            throw new WorkflowTriggerMismatchException(
                $"Workflow definition '{definition.Key}' v{definition.Version} does not support API trigger starts.");
        }

        var triggerData = BuildTriggerData(
            request.Payload,
            apiConfiguration.OperationKey,
            request.Headers,
            request.Query);

        return await _workflowInstanceService.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowKey = definition.Key,
                Version = definition.Version,
                TenantId = request.TenantId,
                ExternalKey = request.ExternalKey,
                IdempotencyKey = request.IdempotencyKey,
                Input = request.Payload,
                TriggerData = triggerData
            },
            cancellationToken);
    }

    private static string BuildTriggerData(
        object? payload,
        string operationKey,
        IReadOnlyDictionary<string, string>? headers,
        IReadOnlyDictionary<string, string>? query) =>
        JsonSerializer.Serialize(new
        {
            source = "api",
            operationKey,
            receivedAtUtc = DateTimeOffset.UtcNow,
            payload,
            headers = headers ?? new Dictionary<string, string>(),
            query = query ?? new Dictionary<string, string>()
        });
}
