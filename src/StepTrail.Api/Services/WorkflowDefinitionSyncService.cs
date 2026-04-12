using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Services;

/// <summary>
/// Runs at startup and ensures every code-first workflow descriptor is persisted to the database.
/// This is idempotent — already-synced definitions are skipped.
/// </summary>
public sealed class WorkflowDefinitionSyncService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWorkflowRegistry _registry;
    private readonly ILogger<WorkflowDefinitionSyncService> _logger;

    public WorkflowDefinitionSyncService(
        IServiceScopeFactory scopeFactory,
        IWorkflowRegistry registry,
        ILogger<WorkflowDefinitionSyncService> logger)
    {
        _scopeFactory = scopeFactory;
        _registry = registry;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();

        foreach (var descriptor in _registry.GetAll())
        {
            var exists = await db.WorkflowDefinitions
                .AnyAsync(w => w.Key == descriptor.Key && w.Version == descriptor.Version, cancellationToken);

            if (exists)
            {
                _logger.LogDebug("Workflow '{Key}' v{Version} already synced — skipping", descriptor.Key, descriptor.Version);
                continue;
            }

            var definition = new WorkflowDefinition
            {
                Id = Guid.NewGuid(),
                Key = descriptor.Key,
                Version = descriptor.Version,
                Name = descriptor.Name,
                Description = descriptor.Description,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.WorkflowDefinitions.Add(definition);

            foreach (var step in descriptor.Steps.OrderBy(s => s.Order))
            {
                db.WorkflowDefinitionSteps.Add(new WorkflowDefinitionStep
                {
                    Id = Guid.NewGuid(),
                    WorkflowDefinitionId = definition.Id,
                    StepKey = step.StepKey,
                    StepType = step.StepType,
                    Order = step.Order,
                    MaxAttempts = step.MaxAttempts,
                    RetryDelaySeconds = step.RetryDelaySeconds,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synced workflow definition '{Key}' v{Version} ({StepCount} steps)",
                descriptor.Key, descriptor.Version, descriptor.Steps.Count);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
