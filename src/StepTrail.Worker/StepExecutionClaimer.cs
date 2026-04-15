using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Worker;

/// <summary>
/// Atomically claims one pending step execution for the given worker.
/// Uses SELECT FOR UPDATE SKIP LOCKED so multiple workers never process the same step.
/// </summary>
public sealed class StepExecutionClaimer
{
    private readonly StepTrailDbContext _db;
    private readonly int _defaultLockExpirySeconds;

    public StepExecutionClaimer(StepTrailDbContext db, IConfiguration configuration)
    {
        _db = db;
        _defaultLockExpirySeconds = configuration.GetValue<int>("Worker:DefaultLockExpirySeconds", 300);
    }

    /// <summary>
    /// Finds the oldest pending step execution that is due, locks and marks it Running.
    /// Returns null if nothing is available.
    /// </summary>
    public async Task<WorkflowStepExecution?> TryClaimAsync(string workerId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingStatus = WorkflowStepExecutionStatus.Pending.ToString();
        var waitingStatus = WorkflowStepExecutionStatus.Waiting.ToString();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            // Atomically find the oldest due Pending or Waiting execution and lock the row.
            // SKIP LOCKED means concurrent workers skip rows already being claimed.
            var execution = await _db.WorkflowStepExecutions
                .FromSqlInterpolated($"""
                    SELECT * FROM workflow_step_executions
                    WHERE (status = {pendingStatus} OR status = {waitingStatus})
                      AND scheduled_at <= {now}
                    ORDER BY scheduled_at ASC
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED
                    """)
                .FirstOrDefaultAsync(ct);

            if (execution is null)
            {
                await tx.RollbackAsync(ct);
                return null;
            }

            // Transition step execution: Pending or Waiting -> Running
            execution.Status = WorkflowStepExecutionStatus.Running;
            execution.LockedAt = now;
            execution.LockedBy = workerId;
            execution.LockExpiresAt = now.AddSeconds(_defaultLockExpirySeconds);
            execution.StartedAt ??= now;
            execution.UpdatedAt = now;

            // Transition parent instance: Pending or AwaitingRetry → Running
            var instance = await _db.WorkflowInstances.FindAsync([execution.WorkflowInstanceId], ct);
            if (instance is not null && instance.Status is WorkflowInstanceStatus.Pending or WorkflowInstanceStatus.AwaitingRetry)
            {
                instance.Status = WorkflowInstanceStatus.Running;
                instance.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            return execution;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
