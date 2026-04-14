using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;
using StepTrail.Worker.Alerts;

namespace StepTrail.Worker;

/// <summary>
/// Persists a step failure and applies the retry / workflow-fail policy.
/// Shared by StepExecutionProcessor (live failures/timeouts) and StuckExecutionDetector (orphan recovery).
/// After persisting, fires alerts via AlertService for permanent failures and orphaned steps.
/// </summary>
public sealed class StepFailureService
{
    private readonly StepTrailDbContext _db;
    private readonly AlertService _alertService;
    private readonly ILogger<StepFailureService> _logger;

    public StepFailureService(StepTrailDbContext db, AlertService alertService, ILogger<StepFailureService> logger)
    {
        _db = db;
        _alertService = alertService;
        _logger = logger;
    }

    /// <summary>
    /// Marks <paramref name="execution"/> as Failed, writes <paramref name="failureEventType"/>,
    /// then either schedules a retry attempt or permanently fails the workflow instance.
    /// Saves changes to the database before returning.
    /// </summary>
    public async Task HandleAsync(
        WorkflowStepExecution execution,
        string error,
        string failureEventType,
        DateTimeOffset now,
        CancellationToken ct,
        string? output = null,
        int maxAttempts = 1,
        int retryDelaySeconds = 0,
        string? workflowKey = null)
    {
        execution.Status = WorkflowStepExecutionStatus.Failed;
        execution.Error = error;
        execution.Output = output;   // preserve response data (e.g. HTTP status/body) for debugging
        execution.CompletedAt = now;
        execution.UpdatedAt = now;

        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = execution.WorkflowInstanceId,
            StepExecutionId = execution.Id,
            EventType = failureEventType,
            CreatedAt = now
        });

        bool workflowFailed = false;

        if (execution.Attempt < maxAttempts)
        {
            var retryAt = now.AddSeconds(retryDelaySeconds);

            var retryExecution = new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                WorkflowDefinitionStepId = execution.WorkflowDefinitionStepId,
                ExecutableStepDefinitionId = execution.ExecutableStepDefinitionId,
                StepKey = execution.StepKey,
                StepOrder = execution.StepOrder,
                StepType = execution.StepType,
                StepConfiguration = execution.StepConfiguration,
                RetryPolicyOverrideKey = execution.RetryPolicyOverrideKey,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = execution.Attempt + 1,
                Input = execution.Input,
                ScheduledAt = retryAt,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.WorkflowStepExecutions.Add(retryExecution);

            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = retryExecution.Id,
                EventType = WorkflowEventTypes.StepRetryScheduled,
                CreatedAt = now
            });

            _logger.LogWarning(
                "Step {StepKey} attempt {Attempt}/{MaxAttempts} failed [{EventType}] — " +
                "retry {NextAttempt} scheduled at {RetryAt}",
                execution.StepKey, execution.Attempt, maxAttempts,
                failureEventType, execution.Attempt + 1, retryAt);
        }
        else
        {
            var instance = await _db.WorkflowInstances
                .FindAsync([execution.WorkflowInstanceId], ct)
                ?? throw new InvalidOperationException(
                    $"WorkflowInstance {execution.WorkflowInstanceId} not found.");

            instance.Status = WorkflowInstanceStatus.Failed;
            instance.CompletedAt = now;
            instance.UpdatedAt = now;

            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = null,
                EventType = WorkflowEventTypes.WorkflowFailed,
                CreatedAt = now
            });

            workflowFailed = true;

            _logger.LogError(
                "Step {StepKey} failed after {MaxAttempts} attempt(s) [{EventType}] — " +
                "workflow instance {InstanceId} is now Failed",
                execution.StepKey, maxAttempts, failureEventType, execution.WorkflowInstanceId);
        }

        await _db.SaveChangesAsync(ct);

        // Fire alerts after the DB commit so we never alert on a rolled-back failure.
        workflowKey ??= await ResolveWorkflowKeyAsync(execution.WorkflowInstanceId, execution.WorkflowDefinitionStepId, ct);

        if (failureEventType == WorkflowEventTypes.StepOrphaned)
        {
            await _alertService.SendAsync(new AlertPayload
            {
                AlertType = "StepOrphaned",
                WorkflowInstanceId = execution.WorkflowInstanceId,
                WorkflowKey = workflowKey,
                StepKey = execution.StepKey,
                Attempt = execution.Attempt,
                Error = error,
                OccurredAt = now
            }, ct);
        }

        if (workflowFailed)
        {
            await _alertService.SendAsync(new AlertPayload
            {
                AlertType = "WorkflowFailed",
                WorkflowInstanceId = execution.WorkflowInstanceId,
                WorkflowKey = workflowKey,
                StepKey = execution.StepKey,
                Attempt = execution.Attempt,
                Error = error,
                OccurredAt = now
            }, ct);
        }
    }

    private async Task<string> ResolveWorkflowKeyAsync(Guid workflowInstanceId, Guid? workflowDefinitionStepId, CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == workflowInstanceId, ct);

        if (!string.IsNullOrWhiteSpace(instance?.WorkflowDefinitionKey))
            return instance.WorkflowDefinitionKey!;

        if (workflowDefinitionStepId.HasValue)
        {
            var stepDefinition = await _db.WorkflowDefinitionSteps
                .AsNoTracking()
                .FirstOrDefaultAsync(step => step.Id == workflowDefinitionStepId.Value, ct);

            if (stepDefinition is null)
                return workflowInstanceId.ToString();

            var definition = await _db.WorkflowDefinitions
                .AsNoTracking()
                .FirstOrDefaultAsync(workflow => workflow.Id == stepDefinition.WorkflowDefinitionId, ct);

            if (!string.IsNullOrWhiteSpace(definition?.Key))
                return definition.Key;
        }

        return workflowInstanceId.ToString();
    }
}
