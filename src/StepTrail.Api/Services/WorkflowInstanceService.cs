using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StepTrail.Api.Models;
using StepTrail.Shared;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Services;

public sealed class WorkflowInstanceService
{
    private readonly StepTrailDbContext _db;
    private readonly IWorkflowRegistry _registry;

    public WorkflowInstanceService(StepTrailDbContext db, IWorkflowRegistry registry)
    {
        _db = db;
        _registry = registry;
    }

    public async Task<(StartWorkflowResponse Response, bool Created)> StartAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Resolve workflow descriptor
        var descriptor = request.Version.HasValue
            ? _registry.Find(request.WorkflowKey, request.Version.Value)
            : _registry.FindLatest(request.WorkflowKey);

        if (descriptor is null)
        {
            throw new WorkflowNotFoundException(
                $"Workflow '{request.WorkflowKey}'" +
                (request.Version.HasValue ? $" v{request.Version}" : " (latest)") +
                " is not registered.");
        }

        // 2. Validate tenant exists
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == request.TenantId, cancellationToken);
        if (!tenantExists)
            throw new TenantNotFoundException($"Tenant '{request.TenantId}' not found.");

        // 3. Check idempotency — return existing instance if key already used
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.IdempotencyRecords
                .Include(r => r.WorkflowInstance)
                    .ThenInclude(i => i.StepExecutions)
                .FirstOrDefaultAsync(
                    r => r.TenantId == request.TenantId && r.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                var firstExecution = existing.WorkflowInstance.StepExecutions
                    .OrderBy(s => s.CreatedAt)
                    .First();

                return (MapToResponse(existing.WorkflowInstance, descriptor, firstExecution.Id, wasAlreadyStarted: true), false);
            }
        }

        // 4. Load the workflow definition and first step from DB
        var definition = await _db.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Key == descriptor.Key && w.Version == descriptor.Version, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Workflow definition '{descriptor.Key}' v{descriptor.Version} not found in DB. " +
                "Ensure WorkflowDefinitionSyncService has run.");

        var firstStep = await _db.WorkflowDefinitionSteps
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .OrderBy(s => s.Order)
            .FirstAsync(cancellationToken);

        // 5. Create instance + first step execution + idempotency record + event atomically
        var now = DateTimeOffset.UtcNow;

        var inputJson = request.Input is null
            ? null
            : JsonSerializer.Serialize(request.Input);

        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            WorkflowDefinitionId = definition.Id,
            ExternalKey = request.ExternalKey,
            IdempotencyKey = request.IdempotencyKey,
            Status = WorkflowInstanceStatus.Pending,
            Input = inputJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        var stepExecution = new WorkflowStepExecution
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instance.Id,
            WorkflowDefinitionStepId = firstStep.Id,
            StepKey = firstStep.StepKey,
            Status = WorkflowStepExecutionStatus.Pending,
            Attempt = 1,
            Input = inputJson,
            ScheduledAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        var startedEvent = new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instance.Id,
            EventType = WorkflowEventTypes.WorkflowStarted,
            CreatedAt = now
        };

        _db.WorkflowInstances.Add(instance);
        _db.WorkflowStepExecutions.Add(stepExecution);
        _db.WorkflowEvents.Add(startedEvent);

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            _db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                TenantId = request.TenantId,
                IdempotencyKey = request.IdempotencyKey,
                WorkflowInstanceId = instance.Id,
                CreatedAt = now
            });
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: "23505" }
                  && !string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            // A concurrent request with the same idempotency key committed first.
            // Clear tracked entities to allow a clean re-query, then return the existing instance.
            _db.ChangeTracker.Clear();

            var existing = await _db.IdempotencyRecords
                .Include(r => r.WorkflowInstance)
                    .ThenInclude(i => i.StepExecutions)
                .FirstOrDefaultAsync(
                    r => r.TenantId == request.TenantId && r.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            // Constraint violated but record missing — should be impossible; rethrow original.
            if (existing is null) throw;

            var firstExecution = existing.WorkflowInstance.StepExecutions
                .OrderBy(s => s.CreatedAt)
                .First();

            return (MapToResponse(existing.WorkflowInstance, descriptor, firstExecution.Id, wasAlreadyStarted: true), false);
        }

        return (MapToResponse(instance, descriptor, stepExecution.Id, wasAlreadyStarted: false), true);
    }

    private static StartWorkflowResponse MapToResponse(
        WorkflowInstance instance,
        WorkflowDescriptor descriptor,
        Guid firstStepExecutionId,
        bool wasAlreadyStarted) => new()
    {
        Id = instance.Id,
        WorkflowKey = descriptor.Key,
        Version = descriptor.Version,
        TenantId = instance.TenantId,
        Status = instance.Status.ToString(),
        ExternalKey = instance.ExternalKey,
        IdempotencyKey = instance.IdempotencyKey,
        FirstStepExecutionId = firstStepExecutionId,
        CreatedAt = instance.CreatedAt,
        WasAlreadyStarted = wasAlreadyStarted
    };
}

public sealed class WorkflowNotFoundException : Exception
{
    public WorkflowNotFoundException(string message) : base(message) { }
}

public sealed class TenantNotFoundException : Exception
{
    public TenantNotFoundException(string message) : base(message) { }
}
