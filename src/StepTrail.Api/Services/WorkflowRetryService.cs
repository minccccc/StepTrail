using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Services;

/// <summary>
/// Manual retry and replay operations for failed or completed workflow instances.
/// Both methods use SELECT FOR UPDATE so concurrent requests serialize on the instance row:
/// the second caller will see the updated status and receive a 409 rather than creating a
/// duplicate pending execution.
/// </summary>
public sealed class WorkflowRetryService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly StepTrailDbContext _db;
    private readonly IWorkflowDefinitionRepository _workflowDefinitionRepository;

    public WorkflowRetryService(
        StepTrailDbContext db,
        IWorkflowDefinitionRepository workflowDefinitionRepository)
    {
        _db = db;
        _workflowDefinitionRepository = workflowDefinitionRepository;
    }

    /// <summary>
    /// Reschedules execution from the most recently failed step.
    /// The attempt counter resets to 1, giving the step a full fresh retry budget.
    /// Only valid for instances in the Failed state.
    /// </summary>
    public async Task<WorkflowRetryResponse> RetryAsync(Guid instanceId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var instance = await _db.WorkflowInstances
                .FromSqlInterpolated($"SELECT * FROM workflow_instances WHERE id = {instanceId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new WorkflowInstanceNotFoundException(
                    $"Workflow instance '{instanceId}' not found.");

            if (instance.Status != WorkflowInstanceStatus.Failed)
                throw new InvalidWorkflowStateException(
                    $"Cannot retry a workflow instance in '{instance.Status}' status. " +
                    "Only Failed instances can be retried.");

            var failedExecution = await _db.WorkflowStepExecutions
                .Where(e => e.WorkflowInstanceId == instanceId
                         && e.Status == WorkflowStepExecutionStatus.Failed)
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefaultAsync(ct)
                ?? throw new InvalidWorkflowStateException(
                    $"No failed step execution found for workflow instance '{instanceId}'.");

            var now = DateTimeOffset.UtcNow;

            var newExecution = new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instanceId,
                WorkflowDefinitionStepId = failedExecution.WorkflowDefinitionStepId,
                ExecutableStepDefinitionId = failedExecution.ExecutableStepDefinitionId,
                StepKey = failedExecution.StepKey,
                StepOrder = failedExecution.StepOrder,
                StepType = failedExecution.StepType,
                StepConfiguration = failedExecution.StepConfiguration,
                RetryPolicyOverrideKey = failedExecution.RetryPolicyOverrideKey,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = 1,
                Input = failedExecution.Input,
                ScheduledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            instance.Status = WorkflowInstanceStatus.Running;
            instance.CompletedAt = null;
            instance.UpdatedAt = now;

            _db.WorkflowStepExecutions.Add(newExecution);
            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instanceId,
                StepExecutionId = newExecution.Id,
                EventType = WorkflowEventTypes.WorkflowRetried,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new WorkflowRetryResponse
            {
                InstanceId = instanceId,
                InstanceStatus = instance.Status.ToString(),
                NewStepExecutionId = newExecution.Id,
                StepKey = newExecution.StepKey
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Replays the workflow from step 1, creating a fresh execution chain.
    /// Valid for instances in the Failed or Completed state.
    /// Previous step executions are preserved in history.
    /// </summary>
    public async Task<WorkflowRetryResponse> ReplayAsync(Guid instanceId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var instance = await _db.WorkflowInstances
                .FromSqlInterpolated($"SELECT * FROM workflow_instances WHERE id = {instanceId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new WorkflowInstanceNotFoundException(
                    $"Workflow instance '{instanceId}' not found.");

            if (instance.Status is not (WorkflowInstanceStatus.Failed or WorkflowInstanceStatus.Completed))
                throw new InvalidWorkflowStateException(
                    $"Cannot replay a workflow instance in '{instance.Status}' status. " +
                    "Only Failed or Completed instances can be replayed.");

            var now = DateTimeOffset.UtcNow;

            var newExecutions = instance.ExecutableWorkflowDefinitionId.HasValue
                ? await MaterializeExecutableReplayExecutionsAsync(instance, now, ct)
                : await MaterializeLegacyReplayExecutionsAsync(instance, now, ct);

            var firstExecution = newExecutions
                .OrderBy(execution => execution.StepOrder ?? int.MaxValue)
                .ThenBy(execution => execution.CreatedAt)
                .First();

            instance.Status = WorkflowInstanceStatus.Running;
            instance.CompletedAt = null;
            instance.UpdatedAt = now;

            _db.WorkflowStepExecutions.AddRange(newExecutions);
            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instanceId,
                StepExecutionId = firstExecution.Id,
                EventType = WorkflowEventTypes.WorkflowReplayed,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new WorkflowRetryResponse
            {
                InstanceId = instanceId,
                InstanceStatus = instance.Status.ToString(),
                NewStepExecutionId = firstExecution.Id,
                StepKey = firstExecution.StepKey
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Archives a workflow instance, hiding it from the default list view.
    /// Valid for any state except Running (cancel first) and Archived.
    /// Any pending step executions are cancelled to prevent worker pickup.
    /// </summary>
    public async Task<WorkflowCancelResponse> ArchiveAsync(Guid instanceId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var instance = await _db.WorkflowInstances
                .FromSqlInterpolated($"SELECT * FROM workflow_instances WHERE id = {instanceId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new WorkflowInstanceNotFoundException(
                    $"Workflow instance '{instanceId}' not found.");

            if (instance.Status == WorkflowInstanceStatus.Running)
                throw new InvalidWorkflowStateException(
                    "Cannot archive a Running instance. Cancel it first.");

            if (instance.Status == WorkflowInstanceStatus.Archived)
                throw new InvalidWorkflowStateException(
                    "Instance is already archived.");

            var now = DateTimeOffset.UtcNow;

            // Cancel any step executions that have not started so the worker doesn't pick them up later.
            var pendingSteps = await _db.WorkflowStepExecutions
                .Where(e => e.WorkflowInstanceId == instanceId
                         && (e.Status == WorkflowStepExecutionStatus.Pending
                             || e.Status == WorkflowStepExecutionStatus.NotStarted))
                .ToListAsync(ct);

            foreach (var step in pendingSteps)
            {
                step.Status = WorkflowStepExecutionStatus.Cancelled;
                step.UpdatedAt = now;
            }

            instance.Status = WorkflowInstanceStatus.Archived;
            instance.UpdatedAt = now;

            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instanceId,
                EventType = WorkflowEventTypes.WorkflowArchived,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new WorkflowCancelResponse
            {
                InstanceId = instanceId,
                InstanceStatus = instance.Status.ToString()
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    /// <summary>
    /// Cancels a workflow instance that has not yet reached a terminal state.
    /// All pending step executions are also cancelled.
    /// Valid for instances in Pending, Running, or Failed state.
    /// </summary>
    public async Task<WorkflowCancelResponse> CancelAsync(Guid instanceId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var instance = await _db.WorkflowInstances
                .FromSqlInterpolated($"SELECT * FROM workflow_instances WHERE id = {instanceId} FOR UPDATE")
                .FirstOrDefaultAsync(ct)
                ?? throw new WorkflowInstanceNotFoundException(
                    $"Workflow instance '{instanceId}' not found.");

            if (instance.Status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Cancelled)
                throw new InvalidWorkflowStateException(
                    $"Cannot cancel a workflow instance in '{instance.Status}' status.");

            var now = DateTimeOffset.UtcNow;

            // Cancel all step executions that have not started so the worker does not pick them up.
            var pendingSteps = await _db.WorkflowStepExecutions
                .Where(e => e.WorkflowInstanceId == instanceId
                         && (e.Status == WorkflowStepExecutionStatus.Pending
                             || e.Status == WorkflowStepExecutionStatus.NotStarted))
                .ToListAsync(ct);

            foreach (var step in pendingSteps)
            {
                step.Status = WorkflowStepExecutionStatus.Cancelled;
                step.UpdatedAt = now;
            }

            instance.Status = WorkflowInstanceStatus.Cancelled;
            instance.CompletedAt = now;
            instance.UpdatedAt = now;

            _db.WorkflowEvents.Add(new WorkflowEvent
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instanceId,
                EventType = WorkflowEventTypes.WorkflowCancelled,
                CreatedAt = now
            });

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return new WorkflowCancelResponse
            {
                InstanceId = instanceId,
                InstanceStatus = instance.Status.ToString()
            };
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private async Task<IReadOnlyList<WorkflowStepExecution>> MaterializeExecutableReplayExecutionsAsync(
        WorkflowInstance instance,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var executableDefinition = await _workflowDefinitionRepository.GetByIdAsync(
            instance.ExecutableWorkflowDefinitionId!.Value,
            ct)
            ?? throw new InvalidOperationException(
                $"Executable workflow definition '{instance.ExecutableWorkflowDefinitionId}' not found for replay.");

        instance.WorkflowDefinitionKey ??= executableDefinition.Key;
        instance.WorkflowDefinitionVersion ??= executableDefinition.Version;

        return executableDefinition.StepDefinitions
            .OrderBy(step => step.Order)
            .Select((step, index) => new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instance.Id,
                ExecutableStepDefinitionId = step.Id,
                StepKey = step.Key,
                StepOrder = step.Order,
                StepType = step.Type.ToString(),
                StepConfiguration = SerializeStepConfiguration(step),
                RetryPolicyOverrideKey = step.RetryPolicyOverrideKey,
                Status = index == 0
                    ? WorkflowStepExecutionStatus.Pending
                    : WorkflowStepExecutionStatus.NotStarted,
                Attempt = 1,
                Input = index == 0 ? instance.Input : null,
                ScheduledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            })
            .ToList();
    }

    private async Task<IReadOnlyList<WorkflowStepExecution>> MaterializeLegacyReplayExecutionsAsync(
        WorkflowInstance instance,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (!instance.WorkflowDefinitionId.HasValue)
            throw new InvalidOperationException(
                $"Workflow instance '{instance.Id}' does not reference a replayable workflow definition.");

        var firstStep = await _db.WorkflowDefinitionSteps
            .Where(step => step.WorkflowDefinitionId == instance.WorkflowDefinitionId.Value)
            .OrderBy(step => step.Order)
            .FirstAsync(ct);

        return
        [
            new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = instance.Id,
                WorkflowDefinitionStepId = firstStep.Id,
                StepKey = firstStep.StepKey,
                StepOrder = firstStep.Order,
                StepType = firstStep.StepType,
                StepConfiguration = firstStep.Config,
                Status = WorkflowStepExecutionStatus.Pending,
                Attempt = 1,
                Input = instance.Input,
                ScheduledAt = now,
                CreatedAt = now,
                UpdatedAt = now
            }
        ];
    }

    private static string SerializeStepConfiguration(StepDefinition stepDefinition) =>
        stepDefinition.Type switch
        {
            StepType.HttpRequest => JsonSerializer.Serialize(stepDefinition.HttpRequestConfiguration!, JsonSerializerOptions),
            StepType.Transform => JsonSerializer.Serialize(stepDefinition.TransformConfiguration!, JsonSerializerOptions),
            StepType.Conditional => JsonSerializer.Serialize(stepDefinition.ConditionalConfiguration!, JsonSerializerOptions),
            StepType.Delay => JsonSerializer.Serialize(stepDefinition.DelayConfiguration!, JsonSerializerOptions),
            StepType.SendWebhook => JsonSerializer.Serialize(stepDefinition.SendWebhookConfiguration!, JsonSerializerOptions),
            _ => throw new InvalidOperationException($"Unsupported step type '{stepDefinition.Type}'.")
        };
}

public sealed class WorkflowInstanceNotFoundException : Exception
{
    public WorkflowInstanceNotFoundException(string message) : base(message) { }
}

public sealed class InvalidWorkflowStateException : Exception
{
    public InvalidWorkflowStateException(string message) : base(message) { }
}
