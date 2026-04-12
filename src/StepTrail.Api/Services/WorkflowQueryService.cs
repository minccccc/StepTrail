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
            query = query.Where(i => i.WorkflowDefinition.Key == workflowKey);

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

        // Fetch the most-recent step execution per instance in a single query
        var instanceIds = items.Select(i => i.Id).ToList();
        var recentSteps = await _db.WorkflowStepExecutions
            .Where(e => instanceIds.Contains(e.WorkflowInstanceId))
            .Select(e => new { e.WorkflowInstanceId, e.StepKey, e.CreatedAt })
            .ToListAsync(ct);

        var currentStepLookup = recentSteps
            .GroupBy(e => e.WorkflowInstanceId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.CreatedAt).First().StepKey);

        return new PagedResult<WorkflowInstanceSummary>
        {
            Items = items.Select(i => new WorkflowInstanceSummary
            {
                Id = i.Id,
                TenantId = i.TenantId,
                WorkflowKey = i.WorkflowDefinition.Key,
                WorkflowVersion = i.WorkflowDefinition.Version,
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
            WorkflowKey = instance.WorkflowDefinition.Key,
            WorkflowVersion = instance.WorkflowDefinition.Version,
            Status = instance.Status.ToString(),
            ExternalKey = instance.ExternalKey,
            IdempotencyKey = instance.IdempotencyKey,
            Input = instance.Input,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = instance.UpdatedAt,
            CompletedAt = instance.CompletedAt,
            StepExecutions = instance.StepExecutions
                .OrderBy(e => e.CreatedAt)
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
}
