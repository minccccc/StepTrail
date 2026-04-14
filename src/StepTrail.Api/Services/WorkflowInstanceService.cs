using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StepTrail.Api.Models;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;

namespace StepTrail.Api.Services;

public sealed class WorkflowInstanceService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly StepTrailDbContext _db;
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowDefinitionRepository _workflowDefinitionRepository;

    public WorkflowInstanceService(
        StepTrailDbContext db,
        IWorkflowRegistry registry,
        IWorkflowDefinitionRepository workflowDefinitionRepository)
    {
        _db = db;
        _registry = registry;
        _workflowDefinitionRepository = workflowDefinitionRepository;
    }

    public async Task<(StartWorkflowResponse Response, bool Created)> StartAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate tenant exists
        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == request.TenantId, cancellationToken);
        if (!tenantExists)
            throw new TenantNotFoundException($"Tenant '{request.TenantId}' not found.");

        // 2. Check idempotency — return existing instance if key already used
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
                    .OrderBy(e => e.StepOrder ?? int.MaxValue)
                    .ThenBy(e => e.CreatedAt)
                    .First();

                return (MapToResponse(existing.WorkflowInstance, firstExecution.Id, wasAlreadyStarted: true), false);
            }
        }

        var inputJson = request.Input is null
            ? null
            : JsonSerializer.Serialize(request.Input);

        // 3. Prefer the persisted executable definition model when one exists.
        var executableDefinition = await TryLoadExecutableDefinitionAsync(request, cancellationToken);
        if (executableDefinition is not null)
            return await StartFromExecutableDefinitionAsync(request, executableDefinition, inputJson, cancellationToken);

        // 4. Fall back to the existing legacy registry-based path for workflows that have
        // not yet been migrated into the executable definition model.
        return await StartFromLegacyDefinitionAsync(request, inputJson, cancellationToken);
    }

    private async Task<StepTrail.Shared.Definitions.WorkflowDefinition?> TryLoadExecutableDefinitionAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var workflowKey = request.WorkflowKey.Trim();

        if (request.Version.HasValue)
        {
            var hasExecutableDefinitions = await _db.ExecutableWorkflowDefinitions
                .AsNoTracking()
                .AnyAsync(definition => definition.Key == workflowKey, cancellationToken);

            if (!hasExecutableDefinitions)
                return null;

            var versionedDefinition = await _workflowDefinitionRepository.GetByKeyAndVersionAsync(
                workflowKey,
                request.Version.Value,
                cancellationToken);

            if (versionedDefinition is null)
                throw new WorkflowNotFoundException(
                    $"Workflow definition '{workflowKey}' v{request.Version.Value} was not found.");

            if (versionedDefinition.Status != WorkflowDefinitionStatus.Active)
                throw new WorkflowDefinitionNotActiveException(
                    $"Workflow definition '{workflowKey}' v{request.Version.Value} is not active.");

            return versionedDefinition;
        }

        var activeDefinition = await _workflowDefinitionRepository.GetActiveByKeyAsync(workflowKey, cancellationToken);
        if (activeDefinition is not null)
            return activeDefinition;

        var hasNonActiveDefinition = await _db.ExecutableWorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(definition => definition.Key == workflowKey, cancellationToken);

        if (hasNonActiveDefinition)
            throw new WorkflowDefinitionNotActiveException(
                $"Workflow definition '{workflowKey}' does not have an active version.");

        return null;
    }

    private async Task<(StartWorkflowResponse Response, bool Created)> StartFromExecutableDefinitionAsync(
        StartWorkflowRequest request,
        StepTrail.Shared.Definitions.WorkflowDefinition definition,
        string? inputJson,
        CancellationToken cancellationToken)
    {
        if (definition.Status != WorkflowDefinitionStatus.Active)
            throw new WorkflowDefinitionNotActiveException(
                $"Workflow definition '{definition.Key}' v{definition.Version} is not active.");

        var now = DateTimeOffset.UtcNow;

        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            ExecutableWorkflowDefinitionId = definition.Id,
            WorkflowDefinitionKey = definition.Key,
            WorkflowDefinitionVersion = definition.Version,
            ExternalKey = request.ExternalKey,
            IdempotencyKey = request.IdempotencyKey,
            Status = WorkflowInstanceStatus.Pending,
            TriggerData = request.TriggerData,
            Input = inputJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        var stepExecutions = definition.StepDefinitions
            .OrderBy(step => step.Order)
            .Select((step, index) => MaterializeExecutableStepExecution(instance.Id, step, inputJson, now, index == 0))
            .ToList();

        var startedEvent = new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instance.Id,
            EventType = WorkflowEventTypes.WorkflowStarted,
            CreatedAt = now
        };

        _db.WorkflowInstances.Add(instance);
        _db.WorkflowStepExecutions.AddRange(stepExecutions);
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
            _db.ChangeTracker.Clear();

            var existing = await _db.IdempotencyRecords
                .Include(r => r.WorkflowInstance)
                    .ThenInclude(i => i.StepExecutions)
                .FirstOrDefaultAsync(
                    r => r.TenantId == request.TenantId && r.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            if (existing is null) throw;

            var firstExecution = existing.WorkflowInstance.StepExecutions
                .OrderBy(e => e.StepOrder ?? int.MaxValue)
                .ThenBy(e => e.CreatedAt)
                .First();

            return (MapToResponse(existing.WorkflowInstance, firstExecution.Id, wasAlreadyStarted: true), false);
        }

        return (MapToResponse(instance, stepExecutions[0].Id, wasAlreadyStarted: false), true);
    }

    private async Task<(StartWorkflowResponse Response, bool Created)> StartFromLegacyDefinitionAsync(
        StartWorkflowRequest request,
        string? inputJson,
        CancellationToken cancellationToken)
    {
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

        var definition = await _db.WorkflowDefinitions
            .FirstOrDefaultAsync(w => w.Key == descriptor.Key && w.Version == descriptor.Version, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Workflow definition '{descriptor.Key}' v{descriptor.Version} not found in DB. " +
                "Ensure WorkflowDefinitionSyncService has run.");

        var firstStep = await _db.WorkflowDefinitionSteps
            .Where(s => s.WorkflowDefinitionId == definition.Id)
            .OrderBy(s => s.Order)
            .FirstAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            WorkflowDefinitionId = definition.Id,
            WorkflowDefinitionKey = definition.Key,
            WorkflowDefinitionVersion = definition.Version,
            ExternalKey = request.ExternalKey,
            IdempotencyKey = request.IdempotencyKey,
            Status = WorkflowInstanceStatus.Pending,
            TriggerData = request.TriggerData,
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
            StepOrder = firstStep.Order,
            StepType = firstStep.StepType,
            StepConfiguration = firstStep.Config,
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
            _db.ChangeTracker.Clear();

            var existing = await _db.IdempotencyRecords
                .Include(r => r.WorkflowInstance)
                    .ThenInclude(i => i.StepExecutions)
                .FirstOrDefaultAsync(
                    r => r.TenantId == request.TenantId && r.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            if (existing is null) throw;

            var firstExecution = existing.WorkflowInstance.StepExecutions
                .OrderBy(e => e.StepOrder ?? int.MaxValue)
                .ThenBy(e => e.CreatedAt)
                .First();

            return (MapToResponse(existing.WorkflowInstance, firstExecution.Id, wasAlreadyStarted: true), false);
        }

        return (MapToResponse(instance, stepExecution.Id, wasAlreadyStarted: false), true);
    }

    private static WorkflowStepExecution MaterializeExecutableStepExecution(
        Guid workflowInstanceId,
        StepDefinition stepDefinition,
        string? inputJson,
        DateTimeOffset now,
        bool isFirstStep) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = workflowInstanceId,
            ExecutableStepDefinitionId = stepDefinition.Id,
            StepKey = stepDefinition.Key,
            StepOrder = stepDefinition.Order,
            StepType = stepDefinition.Type.ToString(),
            StepConfiguration = SerializeStepConfiguration(stepDefinition),
            RetryPolicyOverrideKey = stepDefinition.RetryPolicyOverrideKey,
            Status = isFirstStep ? WorkflowStepExecutionStatus.Pending : WorkflowStepExecutionStatus.NotStarted,
            Attempt = 1,
            Input = isFirstStep ? inputJson : null,
            ScheduledAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

    private static string SerializeStepConfiguration(StepDefinition stepDefinition) =>
        stepDefinition.Type switch
        {
            StepType.HttpRequest => JsonSerializer.Serialize(stepDefinition.HttpRequestConfiguration!, JsonSerializerOptions),
            StepType.Transform => JsonSerializer.Serialize(stepDefinition.TransformConfiguration!, JsonSerializerOptions),
            StepType.Conditional => JsonSerializer.Serialize(stepDefinition.ConditionalConfiguration!, JsonSerializerOptions),
            StepType.Delay => JsonSerializer.Serialize(stepDefinition.DelayConfiguration!, JsonSerializerOptions),
            StepType.SendWebhook => JsonSerializer.Serialize(stepDefinition.SendWebhookConfiguration!, JsonSerializerOptions),
            _ => throw new InvalidOperationException($"Unsupported step type '{stepDefinition.Type}'.")
        };

    private static StartWorkflowResponse MapToResponse(
        WorkflowInstance instance,
        Guid firstStepExecutionId,
        bool wasAlreadyStarted) => new()
    {
        Id = instance.Id,
        WorkflowKey = instance.WorkflowDefinitionKey
            ?? instance.WorkflowDefinition?.Key
            ?? string.Empty,
        Version = instance.WorkflowDefinitionVersion
            ?? instance.WorkflowDefinition?.Version
            ?? 0,
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

public sealed class WorkflowDefinitionNotActiveException : Exception
{
    public WorkflowDefinitionNotActiveException(string message) : base(message) { }
}

public sealed class TenantNotFoundException : Exception
{
    public TenantNotFoundException(string message) : base(message) { }
}
