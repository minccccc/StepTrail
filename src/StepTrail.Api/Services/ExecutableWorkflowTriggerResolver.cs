using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Services;

/// <summary>
/// Resolves executable workflow definitions for trigger-specific start paths.
/// Trigger services use this to pin the exact active definition version before
/// converging into the shared runtime instance creation flow.
/// </summary>
public sealed class ExecutableWorkflowTriggerResolver
{
    private readonly StepTrailDbContext _db;
    private readonly IWorkflowDefinitionRepository _workflowDefinitionRepository;

    public ExecutableWorkflowTriggerResolver(
        StepTrailDbContext db,
        IWorkflowDefinitionRepository workflowDefinitionRepository)
    {
        _db = db;
        _workflowDefinitionRepository = workflowDefinitionRepository;
    }

    public async Task<WorkflowDefinition> ResolveActiveAsync(
        string workflowKey,
        int? version,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workflowKey))
            throw new ArgumentException("Workflow key must not be empty.", nameof(workflowKey));

        var normalizedWorkflowKey = workflowKey.Trim();

        if (version.HasValue)
        {
            var versionedDefinition = await _workflowDefinitionRepository.GetByKeyAndVersionAsync(
                normalizedWorkflowKey,
                version.Value,
                cancellationToken);

            if (versionedDefinition is null)
            {
                throw new WorkflowNotFoundException(
                    $"Workflow definition '{normalizedWorkflowKey}' v{version.Value} was not found.");
            }

            if (versionedDefinition.Status != WorkflowDefinitionStatus.Active)
            {
                throw new WorkflowDefinitionNotActiveException(
                    $"Workflow definition '{normalizedWorkflowKey}' v{version.Value} is not active.");
            }

            return versionedDefinition;
        }

        var activeDefinition = await _workflowDefinitionRepository.GetActiveByKeyAsync(
            normalizedWorkflowKey,
            cancellationToken);

        if (activeDefinition is not null)
            return activeDefinition;

        var hasAnyDefinition = await _db.ExecutableWorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(definition => definition.Key == normalizedWorkflowKey, cancellationToken);

        if (hasAnyDefinition)
        {
            throw new WorkflowDefinitionNotActiveException(
                $"Workflow definition '{normalizedWorkflowKey}' does not have an active version.");
        }

        throw new WorkflowNotFoundException(
            $"Active workflow definition '{normalizedWorkflowKey}' was not found.");
    }

    public async Task<WorkflowDefinition> ResolveActiveWebhookAsync(
        string routeKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(routeKey))
            throw new ArgumentException("Webhook route key must not be empty.", nameof(routeKey));

        var normalizedRouteKey = routeKey.Trim();
        var activeDefinition = await _workflowDefinitionRepository.GetActiveWebhookByRouteKeyAsync(
            normalizedRouteKey,
            cancellationToken);

        if (activeDefinition is not null)
            return activeDefinition;

        throw new WorkflowNotFoundException(
            $"Active webhook endpoint '{normalizedRouteKey}' was not found.");
    }
}
