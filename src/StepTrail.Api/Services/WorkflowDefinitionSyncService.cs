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
                    TimeoutSeconds = step.TimeoutSeconds,
                    Config = step.Config,
                    CreatedAt = DateTimeOffset.UtcNow
                });
            }

            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Synced workflow definition '{Key}' v{Version} ({StepCount} steps)",
                descriptor.Key, descriptor.Version, descriptor.Steps.Count);

            if (descriptor.RecurrenceIntervalSeconds.HasValue)
                await SyncRecurringScheduleAsync(db, definition, descriptor.RecurrenceIntervalSeconds.Value, cancellationToken);
        }
    }

    private async Task SyncRecurringScheduleAsync(
        StepTrailDbContext db,
        WorkflowDefinition definition,
        int intervalSeconds,
        CancellationToken ct)
    {
        var existing = await db.RecurringWorkflowSchedules
            .FirstOrDefaultAsync(s => s.WorkflowDefinitionId == definition.Id, ct);

        if (existing is null)
        {
            var now = DateTimeOffset.UtcNow;
            db.RecurringWorkflowSchedules.Add(new RecurringWorkflowSchedule
            {
                Id = Guid.NewGuid(),
                WorkflowDefinitionId = definition.Id,
                TenantId = TenantSeedService.DefaultTenantId,
                IntervalSeconds = intervalSeconds,
                IsEnabled = true,
                NextRunAt = now,    // fire on next dispatcher poll
                CreatedAt = now,
                UpdatedAt = now
            });

            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Created recurring schedule for '{Key}' — interval: {Interval}s",
                definition.Key, intervalSeconds);
        }
        else if (existing.IntervalSeconds != intervalSeconds)
        {
            existing.IntervalSeconds = intervalSeconds;
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Updated recurring schedule for '{Key}' — new interval: {Interval}s",
                definition.Key, intervalSeconds);
        }
        else
        {
            _logger.LogDebug(
                "Recurring schedule for '{Key}' already up to date — skipping",
                definition.Key);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
