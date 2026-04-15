using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Shared;
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
        CancellationToken ct)
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
            ExternalKey = instance.ExternalKey,
            IdempotencyKey = instance.IdempotencyKey,
            Input = instance.Input,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            CompletedAt = instance.CompletedAt,
            StepExecutions = instance.StepExecutions
                .OrderBy(e => e.CreatedAt)
                .ThenBy(e => e.StepOrder ?? int.MaxValue)
                .ThenBy(e => e.Attempt)
                .Select(e => new StepExecutionSummary
                {
                    Id = e.Id,
                    StepKey = e.StepKey,
                    Status = e.Status.ToString(),
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
