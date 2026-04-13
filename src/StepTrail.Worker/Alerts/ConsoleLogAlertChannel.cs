namespace StepTrail.Worker.Alerts;

/// <summary>
/// Writes a structured alert entry to the application log.
/// Always active — simulates an email/console notification without external dependencies.
/// </summary>
public sealed class ConsoleLogAlertChannel : IAlertChannel
{
    private readonly ILogger<ConsoleLogAlertChannel> _logger;

    public ConsoleLogAlertChannel(ILogger<ConsoleLogAlertChannel> logger)
        => _logger = logger;

    public Task SendAsync(AlertPayload payload, CancellationToken ct)
    {
        _logger.LogWarning(
            "[ALERT:{AlertType}] Workflow '{WorkflowKey}' instance {InstanceId} | " +
            "Step '{StepKey}' attempt {Attempt} | Error: {Error} | At: {OccurredAt:O}",
            payload.AlertType,
            payload.WorkflowKey,
            payload.WorkflowInstanceId,
            payload.StepKey,
            payload.Attempt,
            payload.Error,
            payload.OccurredAt);

        return Task.CompletedTask;
    }
}
