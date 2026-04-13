namespace StepTrail.Worker;

public sealed class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _workerId;
    private readonly TimeSpan _pollInterval;

    public Worker(
        ILogger<Worker> logger,
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
        _workerId = $"worker-{Guid.NewGuid():N}";
        _pollInterval = TimeSpan.FromSeconds(
            configuration.GetValue<int>("Worker:PollIntervalSeconds", 5));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker {WorkerId} starting (poll interval: {Interval}s)",
            _workerId, _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Detect and requeue orphaned executions before claiming new work.
                await DetectStuckExecutionsAsync(stoppingToken);

                // Fire any recurring schedules that are due.
                await DispatchRecurringWorkflowsAsync(stoppingToken);

                var claimed = await TryClaimAndProcessAsync(stoppingToken);

                // Back off only when there's nothing to do.
                // If a step was claimed, poll again immediately in case more are pending.
                if (!claimed)
                    await Task.Delay(_pollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown — swallow and let the loop exit.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker {WorkerId} unhandled error — backing off", _workerId);
                await Task.Delay(_pollInterval, stoppingToken);
            }
        }

        _logger.LogInformation("Worker {WorkerId} stopped", _workerId);
    }

    private async Task DetectStuckExecutionsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var detector = scope.ServiceProvider.GetRequiredService<StuckExecutionDetector>();
        var count = await detector.DetectAndRequeueAsync(ct);
        if (count > 0)
            _logger.LogInformation(
                "Worker {WorkerId} recovered {Count} orphaned step execution(s)",
                _workerId, count);
    }

    private async Task DispatchRecurringWorkflowsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<RecurringWorkflowDispatcher>();
        var count = await dispatcher.DispatchDueSchedulesAsync(ct);
        if (count > 0)
            _logger.LogInformation(
                "Worker {WorkerId} triggered {Count} recurring workflow instance(s)",
                _workerId, count);
    }

    private async Task<bool> TryClaimAndProcessAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();

        var execution = await claimer.TryClaimAsync(_workerId, ct);

        if (execution is null)
            return false;

        _logger.LogInformation(
            "Worker {WorkerId} claimed step execution {ExecutionId} " +
            "(step: {StepKey}, instance: {InstanceId}, attempt: {Attempt})",
            _workerId, execution.Id, execution.StepKey, execution.WorkflowInstanceId, execution.Attempt);

        var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();
        await processor.ProcessAsync(execution, ct);

        return true;
    }
}
