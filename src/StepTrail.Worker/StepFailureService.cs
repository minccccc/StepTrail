using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Alerts;

namespace StepTrail.Worker;

/// <summary>
/// Persists a step failure and applies the retry / workflow-fail policy.
/// Shared by StepExecutionProcessor (live failures/timeouts) and StuckExecutionDetector (orphan recovery).
/// After persisting, fires alerts via AlertService for permanent failures and orphaned steps.
///
/// Retry gating rules:
///   TransientFailure  → retryable (up to policy MaxAttempts)
///   PermanentFailure  → not retryable, workflow fails immediately
///   InvalidConfiguration → not retryable, workflow fails immediately
///   InputResolutionFailure → not retryable, workflow fails immediately
///   null (platform-level orphan) → retryable (up to policy MaxAttempts)
///   Timeout (isTimeout=true) → retryable only if policy.RetryOnTimeout is true
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
    /// Only transient failures (and platform-level failures where classification is null) are retryable.
    /// Saves changes to the database before returning.
    /// </summary>
    public async Task HandleAsync(
        WorkflowStepExecution execution,
        string error,
        string failureEventType,
        DateTimeOffset now,
        CancellationToken ct,
        string? output = null,
        RetryPolicy? retryPolicy = null,
        string? workflowKey = null,
        StepExecutionFailureClassification? failureClassification = null,
        bool isTimeout = false)
    {
        var policy = retryPolicy ?? RetryPolicy.NoRetry;

        execution.Status = WorkflowStepExecutionStatus.Failed;
        execution.Error = error;
        execution.Output = output;   // preserve response data (e.g. HTTP status/body) for debugging
        execution.FailureClassification = failureClassification?.ToString();
        execution.CompletedAt = now;
        execution.UpdatedAt = now;

        bool workflowFailed = false;
        bool isRetryable = ShouldRetry(failureClassification, isTimeout, policy);
        bool retryScheduled = isRetryable && execution.Attempt < policy.MaxAttempts;

        // Failure event payload: classification, attempt context, and whether a retry was scheduled.
        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = execution.WorkflowInstanceId,
            StepExecutionId = execution.Id,
            EventType = failureEventType,
            Payload = BuildFailureEventPayload(
                failureClassification, execution.Attempt, policy.MaxAttempts, retryScheduled, isTimeout),
            CreatedAt = now
        });

        if (retryScheduled)
        {
            var delaySeconds = policy.ComputeDelaySeconds(execution.Attempt);
            var retryAt = now.AddSeconds(delaySeconds);

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
                RetryPolicyJson = execution.RetryPolicyJson,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = execution.Attempt + 1,
                Input = execution.Input,
                ScheduledAt = retryAt,
                CreatedAt = now,
                UpdatedAt = now
            };
            _db.WorkflowStepExecutions.Add(retryExecution);

            // Transition workflow to AwaitingRetry so operators can see a retry is pending.
            var retryInstance = await _db.WorkflowInstances
                .FindAsync([execution.WorkflowInstanceId], ct)
                ?? throw new InvalidOperationException(
                    $"WorkflowInstance {execution.WorkflowInstanceId} not found.");

            retryInstance.Status = WorkflowInstanceStatus.AwaitingRetry;
            retryInstance.UpdatedAt = now;

            // Retry scheduled event payload: delay details and scheduling metadata.
            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = retryExecution.Id,
                EventType = WorkflowEventTypes.StepRetryScheduled,
                Payload = BuildRetryScheduledEventPayload(
                    execution.Attempt + 1, retryAt, delaySeconds, policy.BackoffStrategy),
                CreatedAt = now
            });

            _logger.LogWarning(
                "Step {StepKey} attempt {Attempt}/{MaxAttempts} failed [{EventType}, {Classification}] — " +
                "retry {NextAttempt} scheduled at {RetryAt} (delay {DelaySeconds}s, {BackoffStrategy})",
                execution.StepKey, execution.Attempt, policy.MaxAttempts,
                failureEventType, failureClassification?.ToString() ?? "Platform",
                execution.Attempt + 1, retryAt, delaySeconds, policy.BackoffStrategy);
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

            if (!isRetryable)
            {
                _logger.LogError(
                    "Step {StepKey} failed with non-retryable classification {Classification} [{EventType}] — " +
                    "workflow instance {InstanceId} is now Failed",
                    execution.StepKey, failureClassification, failureEventType, execution.WorkflowInstanceId);
            }
            else
            {
                _logger.LogError(
                    "Step {StepKey} failed after {MaxAttempts} attempt(s) [{EventType}] — " +
                    "workflow instance {InstanceId} is now Failed",
                    execution.StepKey, policy.MaxAttempts, failureEventType, execution.WorkflowInstanceId);
            }
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

    /// <summary>
    /// Determines whether a failure should be retried based on:
    ///   1. Classification: only TransientFailure and null (platform) are retryable
    ///   2. Timeout: only retryable if policy.RetryOnTimeout is true
    /// </summary>
    private static bool ShouldRetry(
        StepExecutionFailureClassification? classification,
        bool isTimeout,
        RetryPolicy policy)
    {
        if (classification is not (null or StepExecutionFailureClassification.TransientFailure))
            return false;

        if (isTimeout && !policy.RetryOnTimeout)
            return false;

        return true;
    }

    private static string BuildFailureEventPayload(
        StepExecutionFailureClassification? classification,
        int attempt,
        int maxAttempts,
        bool retryScheduled,
        bool isTimeout) =>
        JsonSerializer.Serialize(new
        {
            classification = classification?.ToString(),
            attempt,
            maxAttempts,
            retryScheduled,
            isTimeout
        });

    private static string BuildRetryScheduledEventPayload(
        int nextAttempt,
        DateTimeOffset scheduledAt,
        int delaySeconds,
        BackoffStrategy backoffStrategy) =>
        JsonSerializer.Serialize(new
        {
            nextAttempt,
            scheduledAtUtc = scheduledAt,
            delaySeconds,
            backoffStrategy = backoffStrategy.ToString()
        });

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
