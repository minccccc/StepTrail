using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;
using ExecutableWorkflowDefinition = StepTrail.Shared.Definitions.WorkflowDefinition;

namespace StepTrail.Shared.Runtime;

public sealed class WorkflowStartService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly StepTrailDbContext _db;
    private readonly IWorkflowRegistry _registry;
    private readonly IWorkflowDefinitionRepository _workflowDefinitionRepository;

    public WorkflowStartService(
        StepTrailDbContext db,
        IWorkflowRegistry registry,
        IWorkflowDefinitionRepository workflowDefinitionRepository)
    {
        _db = db;
        _registry = registry;
        _workflowDefinitionRepository = workflowDefinitionRepository;
    }

    public async Task<WorkflowStartResult> StartAsync(
        WorkflowStartRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.WorkflowKey))
            throw new ArgumentException("Workflow key must not be empty.", nameof(request));

        var normalizedWorkflowKey = request.WorkflowKey.Trim();

        var tenantExists = await _db.Tenants.AnyAsync(t => t.Id == request.TenantId, cancellationToken);
        if (!tenantExists)
            throw new WorkflowStartTenantNotFoundException($"Tenant '{request.TenantId}' not found.");

        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _db.IdempotencyRecords
                .Include(r => r.WorkflowInstance)
                    .ThenInclude(i => i.StepExecutions)
                .FirstOrDefaultAsync(
                    r =>
                        r.TenantId == request.TenantId &&
                        r.WorkflowKey == normalizedWorkflowKey &&
                        r.IdempotencyKey == request.IdempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                var firstExecution = existing.WorkflowInstance.StepExecutions
                    .OrderBy(e => e.StepOrder ?? int.MaxValue)
                    .ThenBy(e => e.CreatedAt)
                    .First();

                return MapToResult(existing.WorkflowInstance, firstExecution.Id, wasAlreadyStarted: true, created: false);
            }
        }

        var inputJson = request.Input is null
            ? null
            : JsonSerializer.Serialize(request.Input, JsonSerializerOptions);

        var executableDefinition = await TryLoadExecutableDefinitionAsync(request, cancellationToken);
        if (executableDefinition is not null)
            return await StartFromExecutableDefinitionAsync(request, executableDefinition, inputJson, cancellationToken);

        return await StartFromLegacyDefinitionAsync(request, inputJson, cancellationToken);
    }

    private async Task<ExecutableWorkflowDefinition?> TryLoadExecutableDefinitionAsync(
        WorkflowStartRequest request,
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
            {
                throw new WorkflowStartNotFoundException(
                    $"Workflow definition '{workflowKey}' v{request.Version.Value} was not found.");
            }

            if (versionedDefinition.Status != WorkflowDefinitionStatus.Active)
            {
                throw new WorkflowStartDefinitionNotActiveException(
                    $"Workflow definition '{workflowKey}' v{request.Version.Value} is not active.");
            }

            return versionedDefinition;
        }

        var activeDefinition = await _workflowDefinitionRepository.GetActiveByKeyAsync(workflowKey, cancellationToken);
        if (activeDefinition is not null)
            return activeDefinition;

        var hasNonActiveDefinition = await _db.ExecutableWorkflowDefinitions
            .AsNoTracking()
            .AnyAsync(definition => definition.Key == workflowKey, cancellationToken);

        if (hasNonActiveDefinition)
        {
            throw new WorkflowStartDefinitionNotActiveException(
                $"Workflow definition '{workflowKey}' does not have an active version.");
        }

        return null;
    }

    private async Task<WorkflowStartResult> StartFromExecutableDefinitionAsync(
        WorkflowStartRequest request,
        ExecutableWorkflowDefinition definition,
        string? inputJson,
        CancellationToken cancellationToken)
    {
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
                WorkflowKey = definition.Key,
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
            var existingResult = await TryHandleIdempotencyRaceAsync(
                request.TenantId,
                definition.Key,
                request.IdempotencyKey!,
                cancellationToken);

            if (existingResult is null)
                throw;

            return existingResult;
        }

        return MapToResult(instance, stepExecutions[0].Id, wasAlreadyStarted: false, created: true);
    }

    private async Task<WorkflowStartResult> StartFromLegacyDefinitionAsync(
        WorkflowStartRequest request,
        string? inputJson,
        CancellationToken cancellationToken)
    {
        var descriptor = request.Version.HasValue
            ? _registry.Find(request.WorkflowKey, request.Version.Value)
            : _registry.FindLatest(request.WorkflowKey);

        if (descriptor is null)
        {
            throw new WorkflowStartNotFoundException(
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
                WorkflowKey = definition.Key,
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
            var existingResult = await TryHandleIdempotencyRaceAsync(
                request.TenantId,
                definition.Key,
                request.IdempotencyKey!,
                cancellationToken);

            if (existingResult is null)
                throw;

            return existingResult;
        }

        return MapToResult(instance, stepExecution.Id, wasAlreadyStarted: false, created: true);
    }

    private async Task<WorkflowStartResult?> TryHandleIdempotencyRaceAsync(
        Guid tenantId,
        string workflowKey,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _db.ChangeTracker.Clear();

        var existing = await _db.IdempotencyRecords
            .Include(r => r.WorkflowInstance)
                .ThenInclude(i => i.StepExecutions)
            .FirstOrDefaultAsync(
                r =>
                    r.TenantId == tenantId &&
                    r.WorkflowKey == workflowKey &&
                    r.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existing is null)
            return null;

        var firstExecution = existing.WorkflowInstance.StepExecutions
            .OrderBy(e => e.StepOrder ?? int.MaxValue)
            .ThenBy(e => e.CreatedAt)
            .First();

        return MapToResult(existing.WorkflowInstance, firstExecution.Id, wasAlreadyStarted: true, created: false);
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
            RetryPolicyJson = SerializeRetryPolicy(stepDefinition.RetryPolicy),
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

    private static string? SerializeRetryPolicy(RetryPolicy? retryPolicy) =>
        retryPolicy is null ? null : JsonSerializer.Serialize(retryPolicy, JsonSerializerOptions);

    private static WorkflowStartResult MapToResult(
        WorkflowInstance instance,
        Guid firstStepExecutionId,
        bool wasAlreadyStarted,
        bool created) =>
        new()
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
            WasAlreadyStarted = wasAlreadyStarted,
            Created = created
        };
}
