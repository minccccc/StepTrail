using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;

namespace StepTrail.Worker;

/// <summary>
/// Detects step executions that are stuck in the Running state past their lock expiry.
/// This catches workers that crashed or were killed mid-execution without completing their step.
/// Found executions are failed and re-queued according to the normal retry policy.
/// </summary>
public sealed class StuckExecutionDetector
{
    private readonly StepTrailDbContext _db;
    private readonly StepFailureService _failureService;
    private readonly ILogger<StuckExecutionDetector> _logger;

    public StuckExecutionDetector(
        StepTrailDbContext db,
        StepFailureService failureService,
        ILogger<StuckExecutionDetector> logger)
    {
        _db = db;
        _failureService = failureService;
        _logger = logger;
    }

    /// <summary>
    /// Scans for Running step executions whose lock has expired and requeues them.
    /// Returns the number of orphaned executions that were recovered.
    /// </summary>
    public async Task<int> DetectAndRequeueAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var runningStatus = WorkflowStepExecutionStatus.Running.ToString();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Find Running executions whose lock window has passed.
            // FOR UPDATE SKIP LOCKED prevents two workers from claiming the same orphan.
            var orphans = await _db.WorkflowStepExecutions
                .FromSqlInterpolated($"""
                    SELECT * FROM workflow_step_executions
                    WHERE status = {runningStatus}
                      AND lock_expires_at IS NOT NULL
                      AND lock_expires_at <= {now}
                    ORDER BY lock_expires_at ASC
                    LIMIT 100
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct);

            if (orphans.Count == 0)
            {
                await tx.RollbackAsync(ct);
                return 0;
            }

            foreach (var execution in orphans)
            {
                _logger.LogWarning(
                    "Orphaned step execution {ExecutionId} detected " +
                    "(step: {StepKey}, instance: {InstanceId}, attempt: {Attempt}) - " +
                    "lock held by '{LockedBy}' expired at {LockExpiresAt:O}",
                    execution.Id,
                    execution.StepKey,
                    execution.WorkflowInstanceId,
                    execution.Attempt,
                    execution.LockedBy ?? "unknown",
                    execution.LockExpiresAt);

                var error = $"Step execution timed out - lock held by '{execution.LockedBy ?? "unknown"}' " +
                            $"expired at {execution.LockExpiresAt:yyyy-MM-dd HH:mm:ss} UTC. " +
                            "Worker may have crashed.";

                RetryPolicy? retryPolicy = null;

                if (execution.WorkflowDefinitionStepId.HasValue)
                {
                    var stepDefinition = await _db.WorkflowDefinitionSteps
                        .FindAsync([execution.WorkflowDefinitionStepId.Value], ct)
                        ?? throw new InvalidOperationException(
                            $"WorkflowDefinitionStep {execution.WorkflowDefinitionStepId.Value} not found " +
                            $"while recovering orphan {execution.Id}.");

                    retryPolicy = new RetryPolicy(
                        stepDefinition.MaxAttempts,
                        stepDefinition.RetryDelaySeconds,
                        BackoffStrategy.Fixed,
                        retryOnTimeout: true);
                }

                await _failureService.HandleAsync(
                    execution,
                    error,
                    WorkflowEventTypes.StepOrphaned,
                    now,
                    ct,
                    retryPolicy: retryPolicy);
            }

            await tx.CommitAsync(ct);
            return orphans.Count;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
