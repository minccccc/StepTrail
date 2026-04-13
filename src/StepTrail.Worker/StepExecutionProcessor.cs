using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StepTrail.Shared;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;

namespace StepTrail.Worker;

/// <summary>
/// Executes a claimed step execution: resolves the handler, runs it, and persists the outcome.
/// On success, schedules the next step or completes the workflow if this was the last step.
/// On failure or timeout, delegates to StepFailureService which applies the retry policy.
/// Maintains a heartbeat (StepLeaseRenewer) during execution so healthy steps are never
/// reclaimed by the StuckExecutionDetector.
/// </summary>
public sealed class StepExecutionProcessor
{
    private readonly StepTrailDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly StepFailureService _failureService;
    private readonly ILogger<StepExecutionProcessor> _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _lockWindow;

    public StepExecutionProcessor(
        StepTrailDbContext db,
        IServiceProvider serviceProvider,
        StepFailureService failureService,
        IConfiguration configuration,
        ILogger<StepExecutionProcessor> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _failureService = failureService;
        _logger = logger;
        _heartbeatInterval = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Worker:HeartbeatIntervalSeconds", 60));
        _lockWindow = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Worker:DefaultLockExpirySeconds", 300));
    }

    public async Task ProcessAsync(WorkflowStepExecution execution, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Load the step definition for StepType, Order, and TimeoutSeconds
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

        // Resolve handler and execute, applying per-step timeout if configured
        string? output = null;
        string? error = null;
        bool timedOut = false;

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
                Input = execution.Input,
                Config = stepDef.Config
            };

            // Build an execution token: linked to the worker shutdown token,
            // optionally cancelled after TimeoutSeconds.
            CancellationTokenSource? timeoutCts = null;
            var executionToken = ct;

            if (stepDef.TimeoutSeconds.HasValue)
            {
                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(stepDef.TimeoutSeconds.Value));
                executionToken = timeoutCts.Token;
            }

            // Keep the lock alive while the handler runs so the StuckExecutionDetector
            // does not reclaim this step. Disposed (cancelled) as soon as the handler returns.
            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            await using var lease = new StepLeaseRenewer(
                execution.Id, scopeFactory, _heartbeatInterval, _lockWindow, _logger, ct);

            try
            {
                var result = await handler.ExecuteAsync(context, executionToken);
                output = result.Output;
            }
            finally
            {
                timeoutCts?.Dispose();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Worker is shutting down — propagate so the loop can exit cleanly.
            throw;
        }
        catch (OperationCanceledException) when (stepDef.TimeoutSeconds.HasValue)
        {
            // Timeout CTS fired; the handler exceeded its configured limit.
            timedOut = true;
            error = $"Step timed out after {stepDef.TimeoutSeconds.Value}s.";
            _logger.LogWarning(
                "Step {StepKey} (execution {ExecutionId}) timed out after {Timeout}s",
                execution.StepKey, execution.Id, stepDef.TimeoutSeconds.Value);
        }
        catch (HttpActivityException ex)
        {
            // Non-2xx HTTP response: preserve the response output (status code + body)
            // so it is persisted on the failed execution and visible in the timeline.
            error  = ex.Message;
            output = ex.ResponseOutput;
            _logger.LogWarning(
                "Step {StepKey} (execution {ExecutionId}) received non-2xx HTTP response",
                execution.StepKey, execution.Id);
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
            await _failureService.HandleAsync(
                execution, stepDef, error,
                timedOut ? WorkflowEventTypes.StepTimedOut : WorkflowEventTypes.StepFailed,
                now, ct,
                output);
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
}
