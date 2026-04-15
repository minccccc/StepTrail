using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using StepTrail.Worker.StepExecutors;

namespace StepTrail.Worker;

/// <summary>
/// Executes a claimed step execution: resolves the step executor, runs it, and persists the outcome.
/// On success, schedules the next step or completes the workflow if this was the last step.
/// On failure or timeout, delegates to StepFailureService which applies the retry policy.
/// Maintains a heartbeat (StepLeaseRenewer) during execution so healthy steps are never
/// reclaimed by the StuckExecutionDetector.
/// </summary>
public sealed class StepExecutionProcessor
{
    private const int DefaultExecutableMaxAttempts = 3;
    private const int DefaultExecutableRetryDelaySeconds = 10;

    private readonly StepTrailDbContext _db;
    private readonly IServiceProvider _serviceProvider;
    private readonly IStepExecutorRegistry _stepExecutorRegistry;
    private readonly StepFailureService _failureService;
    private readonly ILogger<StepExecutionProcessor> _logger;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _lockWindow;

    public StepExecutionProcessor(
        StepTrailDbContext db,
        IServiceProvider serviceProvider,
        IStepExecutorRegistry stepExecutorRegistry,
        StepFailureService failureService,
        IConfiguration configuration,
        ILogger<StepExecutionProcessor> logger)
    {
        _db = db;
        _serviceProvider = serviceProvider;
        _stepExecutorRegistry = stepExecutorRegistry;
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
        var runtimeDefinition = await ResolveRuntimeDefinitionAsync(execution, ct);

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

        string? output = null;
        string? error = null;
        bool timedOut = false;
        var continuation = StepExecutionContinuation.ContinueWorkflow;
        DateTimeOffset? resumeAtUtc = null;

        try
        {
            var executor = _serviceProvider.GetKeyedService<IStepExecutor>(runtimeDefinition.ExecutorKey)
                ?? throw new InvalidOperationException(
                    $"No step executor registered for key '{runtimeDefinition.ExecutorKey}'.");

            var (workflowState, secretValues) = await BuildExecutionContextAsync(
                execution.WorkflowInstanceId, ct);

            var request = new StepExecutionRequest
            {
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = execution.Id,
                WorkflowDefinitionKey = runtimeDefinition.WorkflowKey,
                WorkflowDefinitionVersion = runtimeDefinition.WorkflowVersion,
                StepKey = execution.StepKey,
                Input = execution.Input,
                CurrentOutput = execution.Output,
                StepType = execution.StepType,
                StepConfiguration = runtimeDefinition.Config,
                State = workflowState,
                Secrets = secretValues
            };

            CancellationTokenSource? timeoutCts = null;
            var executionToken = ct;

            if (runtimeDefinition.TimeoutSeconds.HasValue)
            {
                timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtimeDefinition.TimeoutSeconds.Value));
                executionToken = timeoutCts.Token;
            }

            var scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
            await using var lease = new StepLeaseRenewer(
                execution.Id, scopeFactory, _heartbeatInterval, _lockWindow, _logger, ct);

            try
            {
                var result = await executor.ExecuteAsync(request, executionToken);
                if (result.IsSuccess)
                {
                    output = result.Output;
                    continuation = result.Continuation;
                    resumeAtUtc = result.ResumeAtUtc;
                }
                else
                {
                    output = result.Output;
                    error = result.Failure!.Message;

                    _logger.LogWarning(
                        "Step {StepKey} (execution {ExecutionId}) returned classified failure {Classification}: {Message}",
                        execution.StepKey,
                        execution.Id,
                        result.Failure.Classification,
                        result.Failure.Message);
                }
            }
            finally
            {
                timeoutCts?.Dispose();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (runtimeDefinition.TimeoutSeconds.HasValue)
        {
            timedOut = true;
            error = $"Step timed out after {runtimeDefinition.TimeoutSeconds.Value}s.";
            _logger.LogWarning(
                "Step {StepKey} (execution {ExecutionId}) timed out after {Timeout}s",
                execution.StepKey, execution.Id, runtimeDefinition.TimeoutSeconds.Value);
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
            await PersistSuccessAsync(execution, runtimeDefinition, output, continuation, resumeAtUtc, now, ct);
        else
            await _failureService.HandleAsync(
                execution,
                error,
                timedOut ? WorkflowEventTypes.StepTimedOut : WorkflowEventTypes.StepFailed,
                now,
                ct,
                output,
                runtimeDefinition.MaxAttempts,
                runtimeDefinition.RetryDelaySeconds,
                runtimeDefinition.WorkflowKey);
    }

    private async Task PersistSuccessAsync(
        WorkflowStepExecution execution,
        ResolvedStepExecutionDefinition runtimeDefinition,
        string? output,
        StepExecutionContinuation continuation,
        DateTimeOffset? resumeAtUtc,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (resumeAtUtc.HasValue)
        {
            execution.Status = WorkflowStepExecutionStatus.Waiting;
            execution.Output = output;
            execution.Error = null;
            execution.ScheduledAt = resumeAtUtc.Value;
            execution.LockedAt = null;
            execution.LockedBy = null;
            execution.LockExpiresAt = null;
            execution.CompletedAt = null;
            execution.UpdatedAt = now;

            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = execution.Id,
                EventType = WorkflowEventTypes.StepWaiting,
                CreatedAt = now,
                Payload = output
            });

            _logger.LogInformation(
                "Step {StepKey} entered Waiting until {ResumeAtUtc:O}",
                execution.StepKey,
                resumeAtUtc.Value);

            await _db.SaveChangesAsync(ct);
            return;
        }

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

        if (continuation != StepExecutionContinuation.ContinueWorkflow)
        {
            var finalStatus = continuation == StepExecutionContinuation.CancelWorkflow
                ? WorkflowInstanceStatus.Cancelled
                : WorkflowInstanceStatus.Completed;
            var eventType = continuation == StepExecutionContinuation.CancelWorkflow
                ? WorkflowEventTypes.WorkflowCancelled
                : WorkflowEventTypes.WorkflowCompleted;

            await FinalizeWorkflowInstanceWithStatusAsync(
                execution.WorkflowInstanceId,
                execution.StepKey,
                finalStatus,
                eventType,
                now,
                ct);

            await _db.SaveChangesAsync(ct);
            return;
        }

        if (runtimeDefinition.UseMaterializedSteps)
        {
            var nextStepExecution = await _db.WorkflowStepExecutions
                .Where(stepExecution => stepExecution.WorkflowInstanceId == execution.WorkflowInstanceId
                                     && stepExecution.StepOrder == runtimeDefinition.StepOrder + 1
                                     && stepExecution.Status == WorkflowStepExecutionStatus.NotStarted)
                .OrderBy(stepExecution => stepExecution.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (nextStepExecution is not null)
            {
                nextStepExecution.Status = WorkflowStepExecutionStatus.Pending;
                nextStepExecution.Input = execution.Output;
                nextStepExecution.ScheduledAt = now;
                nextStepExecution.UpdatedAt = now;

                _logger.LogInformation(
                    "Step {StepKey} completed - next materialized step {NextStepKey} scheduled",
                    execution.StepKey, nextStepExecution.StepKey);
            }
            else
            {
                await CompleteWorkflowInstanceAsync(execution.WorkflowInstanceId, execution.StepKey, now, ct);
            }

            await _db.SaveChangesAsync(ct);
            return;
        }

        var nextStepDef = await _db.WorkflowDefinitionSteps
            .Where(s => s.WorkflowDefinitionId == runtimeDefinition.LegacyWorkflowDefinitionId
                     && s.Order == runtimeDefinition.StepOrder + 1)
            .FirstOrDefaultAsync(ct);

        if (nextStepDef is not null)
        {
            _db.WorkflowStepExecutions.Add(new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = execution.WorkflowInstanceId,
                WorkflowDefinitionStepId = nextStepDef.Id,
                StepKey = nextStepDef.StepKey,
                StepOrder = nextStepDef.Order,
                StepType = nextStepDef.StepType,
                StepConfiguration = nextStepDef.Config,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = 1,
                Input = execution.Output,
                ScheduledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            });

            _logger.LogInformation(
                "Step {StepKey} completed - next step {NextStepKey} scheduled",
                execution.StepKey, nextStepDef.StepKey);
        }
        else
        {
            await CompleteWorkflowInstanceAsync(execution.WorkflowInstanceId, execution.StepKey, now, ct);
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task CompleteWorkflowInstanceAsync(
        Guid workflowInstanceId,
        string stepKey,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .FindAsync([workflowInstanceId], ct)
            ?? throw new InvalidOperationException(
                $"WorkflowInstance {workflowInstanceId} not found.");

        instance.Status = WorkflowInstanceStatus.Completed;
        instance.CompletedAt = now;
        instance.UpdatedAt = now;

        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = workflowInstanceId,
            StepExecutionId = null,
            EventType = WorkflowEventTypes.WorkflowCompleted,
            CreatedAt = now
        });

        _logger.LogInformation(
            "Step {StepKey} completed - workflow instance {InstanceId} is now Completed",
            stepKey, workflowInstanceId);
    }

    private async Task FinalizeWorkflowInstanceWithStatusAsync(
        Guid workflowInstanceId,
        string stepKey,
        WorkflowInstanceStatus finalStatus,
        string eventType,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .FindAsync([workflowInstanceId], ct)
            ?? throw new InvalidOperationException(
                $"WorkflowInstance {workflowInstanceId} not found.");

        instance.Status = finalStatus;
        instance.CompletedAt = now;
        instance.UpdatedAt = now;

        _db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = workflowInstanceId,
            StepExecutionId = null,
            EventType = eventType,
            CreatedAt = now
        });

        _logger.LogInformation(
            "Step {StepKey} completed - workflow instance {InstanceId} is now {Status}",
            stepKey, workflowInstanceId, finalStatus);
    }

    private async Task<ResolvedStepExecutionDefinition> ResolveRuntimeDefinitionAsync(
        WorkflowStepExecution execution,
        CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == execution.WorkflowInstanceId, ct)
            ?? throw new InvalidOperationException(
                $"WorkflowInstance {execution.WorkflowInstanceId} not found.");

        if (execution.WorkflowDefinitionStepId.HasValue)
        {
            var stepDef = await _db.WorkflowDefinitionSteps
                .FindAsync([execution.WorkflowDefinitionStepId.Value], ct)
                ?? throw new InvalidOperationException(
                    $"WorkflowDefinitionStep {execution.WorkflowDefinitionStepId.Value} not found.");

            return new ResolvedStepExecutionDefinition(
                stepDef.StepType,
                stepDef.Config,
                stepDef.Order,
                MaxAttempts: stepDef.MaxAttempts,
                RetryDelaySeconds: stepDef.RetryDelaySeconds,
                TimeoutSeconds: stepDef.TimeoutSeconds,
                WorkflowKey: instance.WorkflowDefinitionKey ?? await ResolveLegacyWorkflowKeyAsync(stepDef.WorkflowDefinitionId, ct),
                WorkflowVersion: instance.WorkflowDefinitionVersion,
                UseMaterializedSteps: false,
                LegacyWorkflowDefinitionId: stepDef.WorkflowDefinitionId);
        }

        if (execution.StepOrder is null || string.IsNullOrWhiteSpace(execution.StepType))
            throw new InvalidOperationException(
                $"Executable step execution {execution.Id} is missing snapshot metadata.");

        if (!Enum.TryParse<StepType>(execution.StepType, ignoreCase: true, out var stepType))
            throw new InvalidOperationException(
                $"Executable step execution {execution.Id} has unknown step type '{execution.StepType}'.");

        var resolvedExecutor = _stepExecutorRegistry.Resolve(stepType, execution.StepConfiguration);

        return new ResolvedStepExecutionDefinition(
            resolvedExecutor.ExecutorKey,
            resolvedExecutor.StepConfiguration,
            execution.StepOrder.Value,
            // Temporary executable-step defaults until Phase 6 introduces full retry policy handling.
            MaxAttempts: DefaultExecutableMaxAttempts,
            RetryDelaySeconds: DefaultExecutableRetryDelaySeconds,
            TimeoutSeconds: null,
            WorkflowKey: instance.WorkflowDefinitionKey ?? execution.WorkflowInstanceId.ToString(),
            WorkflowVersion: instance.WorkflowDefinitionVersion,
            UseMaterializedSteps: true,
            LegacyWorkflowDefinitionId: null);
    }

    /// <summary>
    /// Assembles the WorkflowState and pre-loads all secrets needed for placeholder resolution.
    /// Called once per step execution before invoking the step executor.
    /// </summary>
    private async Task<(WorkflowState state, IReadOnlyDictionary<string, string> secrets)> BuildExecutionContextAsync(
        Guid instanceId,
        CancellationToken ct)
    {
        var instance = await _db.WorkflowInstances
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == instanceId, ct)
            ?? throw new InvalidOperationException($"WorkflowInstance {instanceId} not found.");

        var executions = await _db.WorkflowStepExecutions
            .AsNoTracking()
            .Where(e => e.WorkflowInstanceId == instanceId)
            .ToListAsync(ct);

        var state = WorkflowStateAssembler.Assemble(instance, executions);

        var secrets = await _db.WorkflowSecrets
            .AsNoTracking()
            .ToDictionaryAsync(s => s.Name, s => s.Value, ct);

        return (state, secrets);
    }

    private async Task<string> ResolveLegacyWorkflowKeyAsync(Guid workflowDefinitionId, CancellationToken ct)
    {
        var definition = await _db.WorkflowDefinitions.FindAsync([workflowDefinitionId], ct);
        return definition?.Key ?? workflowDefinitionId.ToString();
    }

    private sealed record ResolvedStepExecutionDefinition(
        string ExecutorKey,
        string? Config,
        int StepOrder,
        int MaxAttempts,
        int RetryDelaySeconds,
        int? TimeoutSeconds,
        string WorkflowKey,
        int? WorkflowVersion,
        bool UseMaterializedSteps,
        Guid? LegacyWorkflowDefinitionId);
}
