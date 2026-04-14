using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
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
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

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

        try
        {
            var handler = _serviceProvider.GetKeyedService<IStepHandler>(runtimeDefinition.HandlerKey)
                ?? throw new InvalidOperationException(
                    $"No handler registered for step type '{runtimeDefinition.HandlerKey}'.");

            var context = new StepContext
            {
                WorkflowInstanceId = execution.WorkflowInstanceId,
                StepExecutionId = execution.Id,
                StepKey = execution.StepKey,
                Input = execution.Input,
                Config = runtimeDefinition.Config
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
        catch (HttpActivityException ex)
        {
            error = ex.Message;
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
            await PersistSuccessAsync(execution, runtimeDefinition, output, now, ct);
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
                    "Step {StepKey} completed — next materialized step {NextStepKey} scheduled",
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
                "Step {StepKey} completed — next step {NextStepKey} scheduled",
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
            "Step {StepKey} completed — workflow instance {InstanceId} is now Completed",
            stepKey, workflowInstanceId);
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
                UseMaterializedSteps: false,
                LegacyWorkflowDefinitionId: stepDef.WorkflowDefinitionId);
        }

        if (execution.StepOrder is null || string.IsNullOrWhiteSpace(execution.StepType))
            throw new InvalidOperationException(
                $"Executable step execution {execution.Id} is missing snapshot metadata.");

        if (!Enum.TryParse<StepType>(execution.StepType, ignoreCase: true, out var stepType))
            throw new InvalidOperationException(
                $"Executable step execution {execution.Id} has unknown step type '{execution.StepType}'.");

        var (handlerKey, config) = stepType switch
        {
            StepType.HttpRequest => (nameof(HttpActivityHandler), execution.StepConfiguration),
            StepType.SendWebhook => (nameof(HttpActivityHandler), ConvertSendWebhookToHttpActivityConfig(execution.StepConfiguration)),
            StepType.Transform => throw new InvalidOperationException("No executor is registered yet for executable step type 'Transform'."),
            StepType.Conditional => throw new InvalidOperationException("No executor is registered yet for executable step type 'Conditional'."),
            StepType.Delay => throw new InvalidOperationException("No executor is registered yet for executable step type 'Delay'."),
            _ => throw new InvalidOperationException($"Unsupported executable step type '{stepType}'.")
        };

        return new ResolvedStepExecutionDefinition(
            handlerKey,
            config,
            execution.StepOrder.Value,
            MaxAttempts: 1,
            RetryDelaySeconds: 0,
            TimeoutSeconds: null,
            WorkflowKey: instance.WorkflowDefinitionKey ?? execution.WorkflowInstanceId.ToString(),
            UseMaterializedSteps: true,
            LegacyWorkflowDefinitionId: null);
    }

    private async Task<string> ResolveLegacyWorkflowKeyAsync(Guid workflowDefinitionId, CancellationToken ct)
    {
        var definition = await _db.WorkflowDefinitions.FindAsync([workflowDefinitionId], ct);
        return definition?.Key ?? workflowDefinitionId.ToString();
    }

    private static string? ConvertSendWebhookToHttpActivityConfig(string? stepConfiguration)
    {
        if (string.IsNullOrWhiteSpace(stepConfiguration))
            return stepConfiguration;

        var configuration = JsonSerializer.Deserialize<SendWebhookStepConfigurationSnapshot>(stepConfiguration, JsonSerializerOptions)
            ?? throw new InvalidOperationException("Failed to deserialize send webhook step configuration.");

        var httpActivityConfig = new
        {
            Url = configuration.WebhookUrl,
            Method = configuration.Method ?? "POST",
            Headers = configuration.Headers,
            Body = configuration.Body
        };

        return JsonSerializer.Serialize(httpActivityConfig, JsonSerializerOptions);
    }

    private sealed record ResolvedStepExecutionDefinition(
        string HandlerKey,
        string? Config,
        int StepOrder,
        int MaxAttempts,
        int RetryDelaySeconds,
        int? TimeoutSeconds,
        string WorkflowKey,
        bool UseMaterializedSteps,
        Guid? LegacyWorkflowDefinitionId);

    private sealed class SendWebhookStepConfigurationSnapshot
    {
        public string WebhookUrl { get; set; } = string.Empty;
        public string? Method { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
    }
}
