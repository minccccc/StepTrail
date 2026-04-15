using System.Text.Json;
using StepTrail.Api.Models;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Trigger-specific start path for workflows configured with a Manual trigger.
/// Validates the workflow trigger contract, captures manual trigger data, and then
/// converges into the shared runtime instance creation flow.
/// </summary>
public sealed class ManualWorkflowTriggerService
{
    private readonly ExecutableWorkflowTriggerResolver _workflowTriggerResolver;
    private readonly WorkflowInstanceService _workflowInstanceService;

    public ManualWorkflowTriggerService(
        ExecutableWorkflowTriggerResolver workflowTriggerResolver,
        WorkflowInstanceService workflowInstanceService)
    {
        _workflowTriggerResolver = workflowTriggerResolver;
        _workflowInstanceService = workflowInstanceService;
    }

    public async Task<(StartWorkflowResponse Response, bool Created)> StartAsync(
        StartManualWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.WorkflowKey))
            throw new ArgumentException("Workflow key must not be empty.", nameof(request));
        if (request.TenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(request));
        var payload = request.Payload;

        var definition = await _workflowTriggerResolver.ResolveActiveAsync(
            request.WorkflowKey,
            request.Version,
            cancellationToken);
        var manualConfiguration = definition.TriggerDefinition.Type == TriggerType.Manual
            ? definition.TriggerDefinition.ManualConfiguration
            : null;

        if (manualConfiguration is null || string.IsNullOrWhiteSpace(manualConfiguration.EntryPointKey))
        {
            throw new WorkflowTriggerMismatchException(
                $"Workflow definition '{definition.Key}' v{definition.Version} does not support manual trigger starts.");
        }

        var triggerData = BuildTriggerData(payload, manualConfiguration.EntryPointKey, request.ActorId);

        return await _workflowInstanceService.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowKey = definition.Key,
                Version = definition.Version,
                TenantId = request.TenantId,
                ExternalKey = request.ExternalKey,
                IdempotencyKey = request.IdempotencyKey,
                Input = payload,
                TriggerData = triggerData
            },
            cancellationToken);
    }

    private static string BuildTriggerData(object? payload, string entryPointKey, string? actorId) =>
        JsonSerializer.Serialize(new
        {
            source = "manual",
            entryPointKey,
            actorId = string.IsNullOrWhiteSpace(actorId) ? null : actorId.Trim(),
            submittedAtUtc = DateTimeOffset.UtcNow,
            payload
        });
}

public sealed class WorkflowTriggerMismatchException : Exception
{
    public WorkflowTriggerMismatchException(string message) : base(message) { }
}
