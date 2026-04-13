using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Worker;

/// <summary>
/// Claims and fires recurring workflow schedules that are due.
/// Uses SELECT FOR UPDATE SKIP LOCKED so multiple worker instances never double-fire the same schedule.
/// For each fired schedule a new WorkflowInstance + first WorkflowStepExecution is created,
/// and next_run_at is advanced by the schedule's interval.
/// </summary>
public sealed class RecurringWorkflowDispatcher
{
    private readonly StepTrailDbContext _db;
    private readonly ILogger<RecurringWorkflowDispatcher> _logger;

    public RecurringWorkflowDispatcher(StepTrailDbContext db, ILogger<RecurringWorkflowDispatcher> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Finds all enabled schedules whose next_run_at <= now, creates a workflow instance for each,
    /// then advances next_run_at. Returns the number of schedules fired.
    /// </summary>
    public async Task<int> DispatchDueSchedulesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var isEnabled = true;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // Lock all due, enabled schedules — SKIP LOCKED prevents concurrent workers
            // from firing the same schedule twice.
            var schedules = await _db.RecurringWorkflowSchedules
                .FromSqlInterpolated($"""
                    SELECT * FROM recurring_workflow_schedules
                    WHERE is_enabled = {isEnabled}
                      AND next_run_at <= {now}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            if (schedules.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }

            foreach (var schedule in schedules)
                await FireAsync(schedule, now, ct);

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return schedules.Count;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task FireAsync(RecurringWorkflowSchedule schedule, DateTimeOffset now, CancellationToken ct)
    {
        // Load the first step for this workflow definition.
        var firstStep = await _db.WorkflowDefinitionSteps
            .Where(s => s.WorkflowDefinitionId == schedule.WorkflowDefinitionId)
            .OrderBy(s => s.Order)
            .FirstAsync(ct);

        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            TenantId = schedule.TenantId,
            WorkflowDefinitionId = schedule.WorkflowDefinitionId,
            Status = WorkflowInstanceStatus.Pending,
            Input = schedule.Input,
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
            Input = schedule.Input,
            ScheduledAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.WorkflowInstances.Add(instance);
        _db.WorkflowStepExecutions.Add(stepExecution);
        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instance.Id,
            EventType = WorkflowEventTypes.WorkflowStarted,
            CreatedAt = now
        });

        // Advance the schedule — next firing is exactly one interval from now.
        schedule.LastRunAt = now;
        schedule.NextRunAt = now.AddSeconds(schedule.IntervalSeconds);
        schedule.UpdatedAt = now;

        _logger.LogInformation(
            "Recurring schedule {ScheduleId} fired — created instance {InstanceId} " +
            "(definition: {DefinitionId}, next run: {NextRun:O})",
            schedule.Id, instance.Id, schedule.WorkflowDefinitionId, schedule.NextRunAt);
    }
}
