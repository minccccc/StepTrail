using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Runtime.Scheduling;

namespace StepTrail.Worker;

/// <summary>
/// Claims and fires recurring workflow schedules that are due.
/// Uses SELECT FOR UPDATE SKIP LOCKED so multiple worker instances never double-fire the same schedule.
/// For each fired schedule a new workflow instance is created, and next_run_at is advanced by the interval.
/// </summary>
public sealed class RecurringWorkflowDispatcher
{
    private readonly StepTrailDbContext _db;
    private readonly IWorkflowDefinitionRepository _workflowDefinitionRepository;
    private readonly WorkflowStartService _workflowStartService;
    private readonly ILogger<RecurringWorkflowDispatcher> _logger;

    public RecurringWorkflowDispatcher(
        StepTrailDbContext db,
        IWorkflowDefinitionRepository workflowDefinitionRepository,
        WorkflowStartService workflowStartService,
        ILogger<RecurringWorkflowDispatcher> logger)
    {
        _db = db;
        _workflowDefinitionRepository = workflowDefinitionRepository;
        _workflowStartService = workflowStartService;
        _logger = logger;
    }

    public async Task<int> DispatchDueSchedulesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var isEnabled = true;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
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
        if (!string.IsNullOrWhiteSpace(schedule.ExecutableWorkflowKey))
        {
            await FireExecutableAsync(schedule, now, ct);
            return;
        }

        await FireLegacyAsync(schedule, now, ct);
    }

    private async Task FireExecutableAsync(RecurringWorkflowSchedule schedule, DateTimeOffset now, CancellationToken ct)
    {
        var workflowKey = schedule.ExecutableWorkflowKey
            ?? throw new InvalidOperationException($"Recurring schedule '{schedule.Id}' is missing an executable workflow key.");
        var definition = await _workflowDefinitionRepository.GetActiveByKeyAsync(workflowKey, ct);

        if (definition is null || definition.TriggerDefinition.Type != TriggerType.Schedule)
        {
            schedule.IsEnabled = false;
            schedule.UpdatedAt = now;

            _logger.LogWarning(
                "Recurring schedule {ScheduleId} for executable workflow '{WorkflowKey}' is no longer valid and was disabled",
                schedule.Id,
                workflowKey);

            return;
        }

        var scheduleConfiguration = definition.TriggerDefinition.ScheduleConfiguration
            ?? throw new InvalidOperationException(
                $"Executable scheduled workflow '{workflowKey}' is missing schedule configuration.");
        var scheduleInput = new Dictionary<string, object?>
        {
            ["scheduledAtUtc"] = now
        };
        if (scheduleConfiguration.IntervalSeconds.HasValue)
            scheduleInput["intervalSeconds"] = scheduleConfiguration.IntervalSeconds.Value;
        if (!string.IsNullOrWhiteSpace(scheduleConfiguration.CronExpression))
            scheduleInput["cronExpression"] = scheduleConfiguration.CronExpression;
        var result = await _workflowStartService.StartAsync(
            new WorkflowStartRequest
            {
                WorkflowKey = workflowKey,
                Version = definition.Version,
                TenantId = schedule.TenantId,
                Input = scheduleInput,
                TriggerData = JsonSerializer.Serialize(BuildScheduleTriggerData(
                    schedule.Id,
                    workflowKey,
                    now,
                    scheduleConfiguration))
            },
            ct);

        schedule.IntervalSeconds = scheduleConfiguration.IntervalSeconds;
        schedule.CronExpression = scheduleConfiguration.CronExpression;
        schedule.LastRunAt = now;
        schedule.NextRunAt = ScheduleTriggerTimingCalculator.GetNextRunAtAfterExecution(scheduleConfiguration, now);
        schedule.UpdatedAt = now;

        _logger.LogInformation(
            "Recurring executable schedule {ScheduleId} fired and created instance {InstanceId} " +
            "(workflow: {WorkflowKey} v{Version}, next run: {NextRun:O})",
            schedule.Id,
            result.Id,
            workflowKey,
            result.Version,
            schedule.NextRunAt);
    }

    private async Task FireLegacyAsync(RecurringWorkflowSchedule schedule, DateTimeOffset now, CancellationToken ct)
    {
        var workflowDefinitionId = schedule.WorkflowDefinitionId
            ?? throw new InvalidOperationException($"Recurring schedule '{schedule.Id}' is missing a workflow definition id.");
        var intervalSeconds = schedule.IntervalSeconds
            ?? throw new InvalidOperationException(
                $"Recurring schedule '{schedule.Id}' is missing intervalSeconds for a legacy schedule.");
        var firstStep = await _db.WorkflowDefinitionSteps
            .Where(s => s.WorkflowDefinitionId == workflowDefinitionId)
            .OrderBy(s => s.Order)
            .FirstAsync(ct);

        var instance = new WorkflowInstance
        {
            Id = Guid.NewGuid(),
            TenantId = schedule.TenantId,
            WorkflowDefinitionId = workflowDefinitionId,
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

        schedule.LastRunAt = now;
        schedule.NextRunAt = now.AddSeconds(intervalSeconds);
        schedule.UpdatedAt = now;

        _logger.LogInformation(
            "Recurring schedule {ScheduleId} fired and created instance {InstanceId} " +
            "(definition: {DefinitionId}, next run: {NextRun:O})",
            schedule.Id,
            instance.Id,
            workflowDefinitionId,
            schedule.NextRunAt);
    }

    private static Dictionary<string, object?> BuildScheduleTriggerData(
        Guid scheduleId,
        string workflowKey,
        DateTimeOffset triggeredAtUtc,
        ScheduleTriggerConfiguration scheduleConfiguration)
    {
        var triggerData = new Dictionary<string, object?>
        {
            ["source"] = "schedule",
            ["scheduleId"] = scheduleId,
            ["workflowKey"] = workflowKey,
            ["triggeredAtUtc"] = triggeredAtUtc
        };

        if (scheduleConfiguration.IntervalSeconds.HasValue)
            triggerData["intervalSeconds"] = scheduleConfiguration.IntervalSeconds.Value;
        if (!string.IsNullOrWhiteSpace(scheduleConfiguration.CronExpression))
            triggerData["cronExpression"] = scheduleConfiguration.CronExpression;

        return triggerData;
    }
}
