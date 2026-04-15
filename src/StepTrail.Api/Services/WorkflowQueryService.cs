using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Services;

public sealed class WorkflowQueryService
{
    private readonly StepTrailDbContext _db;

    public WorkflowQueryService(StepTrailDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns a paginated list of workflow instances, optionally filtered.
    /// </summary>
    public async Task<PagedResult<WorkflowInstanceSummary>> ListAsync(
        Guid? tenantId,
        string? workflowKey,
        string? status,
        bool includeArchived,
        int page,
        int pageSize,
        CancellationToken ct,
        DateTimeOffset? createdFrom = null,
        DateTimeOffset? createdTo = null,
        string? triggerType = null)
    {
        var query = _db.WorkflowInstances
            .Include(i => i.WorkflowDefinition)
            .AsNoTracking()
            .AsQueryable();

        // Exclude archived instances unless explicitly requested
        if (!includeArchived)
            query = query.Where(i => i.Status != WorkflowInstanceStatus.Archived);

        if (tenantId.HasValue)
            query = query.Where(i => i.TenantId == tenantId.Value);

        if (!string.IsNullOrWhiteSpace(workflowKey))
        {
            query = query.Where(i =>
                i.WorkflowDefinitionKey == workflowKey ||
                (i.WorkflowDefinition != null && i.WorkflowDefinition.Key == workflowKey));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<WorkflowInstanceStatus>(status, ignoreCase: true, out var parsedStatus))
                throw new ArgumentException($"Invalid status value '{status}'.");

            query = query.Where(i => i.Status == parsedStatus);
        }

        if (createdFrom.HasValue)
            query = query.Where(i => i.CreatedAt >= createdFrom.Value);

        if (createdTo.HasValue)
            query = query.Where(i => i.CreatedAt <= createdTo.Value);

        if (!string.IsNullOrWhiteSpace(triggerType))
        {
            if (!Enum.TryParse<TriggerType>(triggerType, ignoreCase: true, out var parsedTriggerType))
                throw new ArgumentException($"Invalid trigger type value '{triggerType}'.");

            // Filter by trigger type through the executable workflow definition's trigger record.
            var definitionIdsWithTriggerType = _db.Set<ExecutableTriggerDefinitionRecord>()
                .Where(t => t.Type == parsedTriggerType)
                .Select(t => t.WorkflowDefinitionId);

            query = query.Where(i =>
                i.ExecutableWorkflowDefinitionId.HasValue &&
                definitionIdsWithTriggerType.Contains(i.ExecutableWorkflowDefinitionId.Value));
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var instanceIds = items.Select(i => i.Id).ToList();
        var stepExecutions = await _db.WorkflowStepExecutions
            .Where(e => instanceIds.Contains(e.WorkflowInstanceId))
            .Select(e => new StepExecutionProjection(
                e.WorkflowInstanceId,
                e.StepKey,
                e.Status,
                e.StepOrder,
                e.Attempt,
                e.CreatedAt,
                e.StartedAt,
                e.CompletedAt))
            .ToListAsync(ct);

        var currentStepLookup = stepExecutions
            .GroupBy(e => e.WorkflowInstanceId)
            .ToDictionary(
                g => g.Key,
                g => ResolveCurrentStep(
                    items.First(instance => instance.Id == g.Key).Status,
                    g.ToList()));

        // Resolve trigger types from executable definitions.
        var executableDefIds = items
            .Where(i => i.ExecutableWorkflowDefinitionId.HasValue)
            .Select(i => i.ExecutableWorkflowDefinitionId!.Value)
            .Distinct()
            .ToList();

        var triggerTypeLookup = executableDefIds.Count > 0
            ? await _db.Set<ExecutableTriggerDefinitionRecord>()
                .Where(t => executableDefIds.Contains(t.WorkflowDefinitionId))
                .ToDictionaryAsync(t => t.WorkflowDefinitionId, t => t.Type.ToString(), ct)
            : new Dictionary<Guid, string>();

        return new PagedResult<WorkflowInstanceSummary>
        {
            Items = items.Select(i => new WorkflowInstanceSummary
            {
                Id = i.Id,
                TenantId = i.TenantId,
                WorkflowKey = i.WorkflowDefinitionKey
                    ?? i.WorkflowDefinition?.Key
                    ?? string.Empty,
                WorkflowVersion = i.WorkflowDefinitionVersion
                    ?? i.WorkflowDefinition?.Version
                    ?? 0,
                Status = i.Status.ToString(),
                TriggerType = i.ExecutableWorkflowDefinitionId.HasValue
                    ? triggerTypeLookup.GetValueOrDefault(i.ExecutableWorkflowDefinitionId.Value)
                    : null,
                ExternalKey = i.ExternalKey,
                IdempotencyKey = i.IdempotencyKey,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt,
                CompletedAt = i.CompletedAt,
                CurrentStep = currentStepLookup.GetValueOrDefault(i.Id)
            }).ToList(),
            Total = total,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary>
    /// Returns the full detail for a single workflow instance, including all step executions.
    /// </summary>
    public async Task<WorkflowInstanceDetail> GetDetailAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .Include(i => i.WorkflowDefinition)
            .Include(i => i.StepExecutions)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new WorkflowInstanceNotFoundException(
                $"Workflow instance '{instanceId}' not found.");

        string? triggerType = null;
        if (instance.ExecutableWorkflowDefinitionId.HasValue)
        {
            triggerType = await _db.Set<ExecutableTriggerDefinitionRecord>()
                .Where(t => t.WorkflowDefinitionId == instance.ExecutableWorkflowDefinitionId.Value)
                .Select(t => t.Type.ToString())
                .FirstOrDefaultAsync(ct);
        }

        return new WorkflowInstanceDetail
        {
            Id = instance.Id,
            TenantId = instance.TenantId,
            WorkflowKey = instance.WorkflowDefinitionKey
                ?? instance.WorkflowDefinition?.Key
                ?? string.Empty,
            WorkflowVersion = instance.WorkflowDefinitionVersion
                ?? instance.WorkflowDefinition?.Version
                ?? 0,
            Status = instance.Status.ToString(),
            TriggerType = triggerType,
            ExternalKey = instance.ExternalKey,
            IdempotencyKey = instance.IdempotencyKey,
            Input = instance.Input,
            TriggerData = instance.TriggerData,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            CompletedAt = instance.CompletedAt,
            CanRetry = instance.Status == WorkflowInstanceStatus.Failed,
            CanReplay = instance.Status is WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Completed,
            CanCancel = instance.Status is not (WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled),
            CanArchive = instance.Status is not (WorkflowInstanceStatus.Running or WorkflowInstanceStatus.AwaitingRetry or WorkflowInstanceStatus.Archived),
            StepExecutions = instance.StepExecutions
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.StepOrder ?? int.MaxValue)
                .ThenBy(e => e.Attempt)
                .Select(e => new StepExecutionSummary
                {
                    Id = e.Id,
                    StepKey = e.StepKey,
                    StepType = e.StepType,
                    Status = e.Status.ToString(),
                    FailureClassification = e.FailureClassification,
                    Attempt = e.Attempt,
                    ScheduledAt = e.ScheduledAt,
                    StartedAt = e.StartedAt,
                    CompletedAt = e.CompletedAt,
                    Output = e.Output,
                    Error = e.Error,
                    CreatedAt = e.CreatedAt
                })
                .ToList()
        };
    }

    /// <summary>
    /// Returns a structured step-by-step trail for the Trail view.
    /// Steps are grouped with their attempt history, latest outcome, and waiting/replay metadata.
    /// </summary>
    public async Task<WorkflowTrail> GetTrailAsync(Guid instanceId, CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .Include(i => i.StepExecutions)
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new WorkflowInstanceNotFoundException(
                $"Workflow instance '{instanceId}' not found.");

        var events = await _db.WorkflowEvents
            .Where(e => e.WorkflowInstanceId == instanceId)
            .AsNoTracking()
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        // Resolve trigger type from the definition.
        string? trailTriggerType = null;
        if (instance.ExecutableWorkflowDefinitionId.HasValue)
        {
            trailTriggerType = await _db.Set<ExecutableTriggerDefinitionRecord>()
                .Where(t => t.WorkflowDefinitionId == instance.ExecutableWorkflowDefinitionId.Value)
                .Select(t => t.Type.ToString())
                .FirstOrDefaultAsync(ct);
        }

        // Trigger summary: the WorkflowStarted event enriched with trigger type.
        var startedEvent = events.FirstOrDefault(e => e.EventType == WorkflowEventTypes.WorkflowStarted);
        var triggerSummary = startedEvent is not null
            ? new TrailTriggerSummary
            {
                EventType = WorkflowEventTypes.WorkflowStarted,
                TriggerType = trailTriggerType,
                OccurredAt = startedEvent.CreatedAt,
                Payload = startedEvent.Payload
            }
            : null;

        // Group step executions by step key, then build structured steps.
        var stepGroups = instance.StepExecutions
            .GroupBy(e => e.StepKey)
            .OrderBy(g => g.Min(e => e.StepOrder ?? int.MaxValue))
            .ThenBy(g => g.Min(e => e.CreatedAt));

        var steps = new List<TrailStep>();

        foreach (var group in stepGroups)
        {
            // Order by CreatedAt (not Attempt) so that rows from manual retry/replay
            // (which reset Attempt to 1) still appear after older attempts.
            var orderedAttempts = group.OrderBy(e => e.CreatedAt).ToList();
            var latest = orderedAttempts[^1];

            // If there is a Pending attempt newer than the latest non-pending, it's a retry.
            var pendingRetry = orderedAttempts
                .LastOrDefault(e => e.Status == WorkflowStepExecutionStatus.Pending);

            DateTimeOffset? nextRetryAt = null;
            if (pendingRetry is not null && orderedAttempts.Count > 1)
                nextRetryAt = pendingRetry.ScheduledAt;

            DateTimeOffset? waitingUntil = null;
            if (latest.Status == WorkflowStepExecutionStatus.Waiting)
                waitingUntil = latest.ScheduledAt;

            steps.Add(new TrailStep
            {
                StepKey = group.Key,
                StepType = latest.StepType,
                StepOrder = latest.StepOrder ?? 0,
                LatestStatus = latest.Status.ToString(),
                LatestFailureClassification = latest.FailureClassification,
                LatestError = latest.Error,
                LatestOutput = latest.Output,
                WaitingUntil = waitingUntil,
                NextRetryAt = nextRetryAt,
                Attempts = orderedAttempts.Select(e => new TrailStepAttempt
                {
                    ExecutionId = e.Id,
                    Attempt = e.Attempt,
                    Status = e.Status.ToString(),
                    FailureClassification = e.FailureClassification,
                    ScheduledAt = e.ScheduledAt,
                    StartedAt = e.StartedAt,
                    CompletedAt = e.CompletedAt,
                    Error = e.Error,
                    Output = e.Output
                }).ToList()
            });
        }

        // Replay events
        var replayEvents = events
            .Where(e => e.EventType == WorkflowEventTypes.WorkflowReplayed)
            .Select(e => new TrailReplayEvent
            {
                OccurredAt = e.CreatedAt,
                Payload = e.Payload
            })
            .ToList();

        return new WorkflowTrail
        {
            InstanceId = instance.Id,
            WorkflowKey = instance.WorkflowDefinitionKey ?? string.Empty,
            WorkflowVersion = instance.WorkflowDefinitionVersion ?? 0,
            Status = instance.Status.ToString(),
            CreatedAt = instance.CreatedAt,
            CompletedAt = instance.CompletedAt,
            Trigger = triggerSummary,
            Steps = steps,
            ReplayEvents = replayEvents
        };
    }

    /// <summary>
    /// Returns all events for a workflow instance in chronological order,
    /// enriched with step key and attempt from the associated step execution.
    /// </summary>
    public async Task<IReadOnlyList<WorkflowTimelineEvent>> GetTimelineAsync(
        Guid instanceId,
        CancellationToken ct)
    {
        var instanceExists = await _db.WorkflowInstances
            .AnyAsync(i => i.Id == instanceId, ct);

        if (!instanceExists)
            throw new WorkflowInstanceNotFoundException(
                $"Workflow instance '{instanceId}' not found.");

        var events = await _db.WorkflowEvents
            .Where(e => e.WorkflowInstanceId == instanceId)
            .Include(e => e.StepExecution)
            .AsNoTracking()
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);

        return events.Select(e => new WorkflowTimelineEvent
        {
            Id = e.Id,
            EventType = e.EventType,
            StepKey = e.StepExecution?.StepKey,
            StepAttempt = e.StepExecution?.Attempt,
            Payload = e.Payload,
            CreatedAt = e.CreatedAt
        }).ToList();
    }

    private static string? ResolveCurrentStep(
        WorkflowInstanceStatus instanceStatus,
        IReadOnlyList<StepExecutionProjection> stepExecutions)
    {
        if (stepExecutions.Count == 0)
            return null;

        var activeStep = stepExecutions
            .Where(step => step.Status is WorkflowStepExecutionStatus.Pending
                                        or WorkflowStepExecutionStatus.Waiting
                                        or WorkflowStepExecutionStatus.Running)
            .OrderBy(step => step.StepOrder ?? int.MaxValue)
            .ThenByDescending(step => step.StartedAt ?? step.CreatedAt)
            .FirstOrDefault();

        if (activeStep is not null)
            return activeStep.StepKey;

        var failedStep = stepExecutions
            .Where(step => step.Status == WorkflowStepExecutionStatus.Failed)
            .OrderByDescending(step => step.CompletedAt ?? step.CreatedAt)
            .ThenByDescending(step => step.Attempt)
            .FirstOrDefault();

        if (instanceStatus == WorkflowInstanceStatus.Failed && failedStep is not null)
            return failedStep.StepKey;

        var terminalStep = stepExecutions
            .Where(step => step.Status is WorkflowStepExecutionStatus.Completed or WorkflowStepExecutionStatus.Cancelled)
            .OrderByDescending(step => step.StepOrder ?? int.MinValue)
            .ThenByDescending(step => step.CompletedAt ?? step.CreatedAt)
            .FirstOrDefault();

        if (instanceStatus == WorkflowInstanceStatus.AwaitingRetry && failedStep is not null)
            return failedStep.StepKey;

        if (instanceStatus is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled or WorkflowInstanceStatus.Archived)
            return terminalStep?.StepKey ?? failedStep?.StepKey;

        var notStartedStep = stepExecutions
            .Where(step => step.Status == WorkflowStepExecutionStatus.NotStarted)
            .OrderBy(step => step.StepOrder ?? int.MaxValue)
            .ThenBy(step => step.CreatedAt)
            .FirstOrDefault();

        return failedStep?.StepKey
            ?? notStartedStep?.StepKey
            ?? terminalStep?.StepKey
            ?? stepExecutions
                .OrderByDescending(step => step.CreatedAt)
                .ThenByDescending(step => step.StepOrder ?? int.MinValue)
                .Select(step => step.StepKey)
                .FirstOrDefault();
    }

    private sealed record StepExecutionProjection(
        Guid WorkflowInstanceId,
        string StepKey,
        WorkflowStepExecutionStatus Status,
        int? StepOrder,
        int Attempt,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt);
}
