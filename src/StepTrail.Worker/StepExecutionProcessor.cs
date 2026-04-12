using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;

namespace StepTrail.Worker;

/// <summary>
/// Executes a claimed step execution: resolves the handler, runs it, and persists the outcome.
/// On success, schedules the next step or completes the workflow if this was the last step.
/// On failure, retries up to MaxAttempts times (with RetryDelaySeconds between attempts).
/// Permanently fails the workflow once all attempts are exhausted.
/// </summary>
public sealed class StepExecutionProcessor
{
    private readonly StepTrailDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StepExecutionProcessor> _logger;

    public StepExecutionProcessor(
        StepTrailDbContext db,
        IServiceProvider serviceProvider,
        ILogger<StepExecutionProcessor> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(WorkflowStepExecution execution, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Load the step definition for StepType and Order
        var stepDef = await _db.WorkflowDefinitionSteps
            .FindAsync([execution.WorkflowDefinitionStepId], ct)
            ?? throw new InvalidOperationException(
                $"WorkflowDefinitionStep {execution.WorkflowDefinitionStepId} not found.");

        // Record that execution has begun
        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = execution.WorkflowInstanceId,
            StepExecutionId = execution.Id,
            EventType = WorkflowEventTypes.StepStarted,
            CreatedAt = now
        });
        await _db.SaveChangesAsync(ct);

        // Resolve handler and execute
        string? output = null;
        string? error = null;

        try
        {
            var handler = _serviceProvider.GetKeyedService<IStepHandler>(stepDef.StepType)
                ?? throw new InvalidOperationException(
                    $"No handler registered for step type '{stepDef.StepType}'.");

            var context = new StepContext
            {
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = execution.Id,
                StepKey = execution.StepKey,
                Input = execution.Input
            };

            var result = await handler.ExecuteAsync(context, ct);
            output = result.Output;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Let the worker loop handle shutdown cleanly
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _logger.LogError(ex,
                "Step {StepKey} (execution {ExecutionId}) threw an unhandled exception",
                execution.StepKey, execution.Id);
        }

        now = DateTimeOffset.UtcNow;

        if (error is null)
            await PersistSuccessAsync(execution, stepDef, output, now, ct);
        else
            await PersistFailureAsync(execution, stepDef, error, now, ct);
    }

    private async Task PersistSuccessAsync(
        WorkflowStepExecution execution,
        WorkflowDefinitionStep stepDef,
        string? output,
        DateTimeOffset now,
        CancellationToken ct)
    {
        execution.Status = WorkflowStepExecutionStatus.Completed;
        execution.Output = output;
        execution.CompletedAt = now;
        execution.UpdatedAt = now;

        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = execution.WorkflowInstanceId,
            StepExecutionId = execution.Id,
            EventType = WorkflowEventTypes.StepCompleted,
            CreatedAt = now
        });

        // Look for the next step in the workflow
        var nextStepDef = await _db.WorkflowDefinitionSteps
            .Where(s => s.WorkflowDefinitionId == stepDef.WorkflowDefinitionId
                     && s.Order == stepDef.Order + 1)
            .FirstOrDefaultAsync(ct);

        if (nextStepDef is not null)
        {
            _db.WorkflowStepExecutions.Add(new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                WorkflowDefinitionStepId = nextStepDef.Id,
                StepKey = nextStepDef.StepKey,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = 1,
                Input = execution.Output,   // previous step's output becomes next step's input
                ScheduledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });

            _logger.LogInformation(
                "Step {StepKey} completed — next step {NextStepKey} scheduled",
                execution.StepKey, nextStepDef.StepKey);
        }
        else
        {
            // Last step — complete the workflow instance
            var instance = await _db.WorkflowInstances
                .FindAsync([execution.WorkflowInstanceId], ct)
                ?? throw new InvalidOperationException(
                    $"WorkflowInstance {execution.WorkflowInstanceId} not found.");

            instance.Status = WorkflowInstanceStatus.Completed;
            instance.CompletedAt = now;
            instance.UpdatedAt = now;

            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = null,
                EventType = WorkflowEventTypes.WorkflowCompleted,
                CreatedAt = now
            });

            _logger.LogInformation(
                "Step {StepKey} completed — workflow instance {InstanceId} is now Completed",
                execution.StepKey, execution.WorkflowInstanceId);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task PersistFailureAsync(
        WorkflowStepExecution execution,
        WorkflowDefinitionStep stepDef,
        string error,
        DateTimeOffset now,
        CancellationToken ct)
    {
        execution.Status = WorkflowStepExecutionStatus.Failed;
        execution.Error = error;
        execution.CompletedAt = now;
        execution.UpdatedAt = now;

        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = execution.WorkflowInstanceId,
            StepExecutionId = execution.Id,
            EventType = WorkflowEventTypes.StepFailed,
            CreatedAt = now
        });

        if (execution.Attempt < stepDef.MaxAttempts)
        {
            // Retries remaining — schedule next attempt
            var retryScheduledAt = now.AddSeconds(stepDef.RetryDelaySeconds);

            var retryExecution = new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                WorkflowDefinitionStepId = execution.WorkflowDefinitionStepId,
                StepKey = execution.StepKey,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = execution.Attempt + 1,
                Input = execution.Input,
                ScheduledAt = retryScheduledAt,
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
                "Step {StepKey} attempt {Attempt}/{MaxAttempts} failed — retry {NextAttempt} scheduled at {RetryAt}",
                execution.StepKey, execution.Attempt, stepDef.MaxAttempts,
                execution.Attempt + 1, retryScheduledAt);
        }
        else
        {
            // All attempts exhausted — fail the workflow
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

            _logger.LogError(
                "Step {StepKey} failed after {MaxAttempts} attempt(s) — workflow instance {InstanceId} is now Failed",
                execution.StepKey, stepDef.MaxAttempts, execution.WorkflowInstanceId);
        }

        await _db.SaveChangesAsync(ct);
    }
}
