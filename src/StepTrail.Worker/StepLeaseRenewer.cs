using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Worker;

/// <summary>
/// Keeps a step execution's lock alive by periodically extending lock_expires_at while
/// the handler is running. This prevents the StuckExecutionDetector from incorrectly
/// treating a legitimately-slow-but-healthy step as orphaned.
///
/// Usage: create before calling handler.ExecuteAsync, dispose (await using) after it returns.
/// A separate DbContext scope is used for each renewal so it does not interfere with
/// the processor's own context or its open transactions.
/// </summary>
internal sealed class StepLeaseRenewer : IAsyncDisposable
{
    private readonly Guid _executionId;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _renewalInterval;
    private readonly TimeSpan _lockWindow;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _backgroundTask;

    public StepLeaseRenewer(
        Guid executionId,
        IServiceScopeFactory scopeFactory,
        TimeSpan renewalInterval,
        TimeSpan lockWindow,
        ILogger logger,
        CancellationToken outerCt)
    {
        _executionId = executionId;
        _scopeFactory = scopeFactory;
        _renewalInterval = renewalInterval;
        _lockWindow = lockWindow;
        _logger = logger;
        _backgroundTask = RunAsync(outerCt);
    }

    private async Task RunAsync(CancellationToken outerCt)
    {
        // Stop when the handler finishes (cts cancelled) OR the worker shuts down (outerCt cancelled).
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerCt, _cts.Token);
        var token = linked.Token;

        while (true)
        {
            try
            {
                await Task.Delay(_renewalInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            try
            {
                await RenewAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Non-fatal — log and continue. The lock window provides a large enough buffer
                // for transient failures (e.g. momentary DB connectivity).
                _logger.LogWarning(ex,
                    "Failed to renew lease for step execution {ExecutionId} — will retry next interval",
                    _executionId);
            }
        }
    }

    private async Task RenewAsync(CancellationToken ct)
    {
        var newExpiry = DateTimeOffset.UtcNow.Add(_lockWindow);

        // Use a fresh scope so this UPDATE is independent of the processor's DbContext.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();

        var updated = await db.WorkflowStepExecutions
            .Where(e => e.Id == _executionId && e.Status == WorkflowStepExecutionStatus.Running)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(e => e.LockExpiresAt, newExpiry)
                    .SetProperty(e => e.UpdatedAt, DateTimeOffset.UtcNow),
                ct);

        if (updated == 1)
            _logger.LogDebug(
                "Lease renewed for step execution {ExecutionId} — new expiry: {ExpiresAt:O}",
                _executionId, newExpiry);
        else
            _logger.LogDebug(
                "Lease renewal for {ExecutionId} affected 0 rows — step likely already completed",
                _executionId);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _backgroundTask; }
        catch (OperationCanceledException) { /* expected */ }
        _cts.Dispose();
    }
}
