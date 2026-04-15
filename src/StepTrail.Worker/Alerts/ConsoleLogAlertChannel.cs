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

    public string ChannelName => "ConsoleLog";

    public Task<AlertDeliveryResult> SendAsync(AlertPayload payload, CancellationToken ct)
    {
        _logger.LogWarning(
            "[ALERT:{AlertType}] Workflow '{WorkflowKey}' v{Version} instance {InstanceId} | " +
            "Step '{StepKey}' attempt {Attempt} | {Message} | Error: {Error} | At: {OccurredAt:O}",
            payload.AlertType,
            payload.WorkflowKey,
            payload.WorkflowVersion,
            payload.WorkflowInstanceId,
            payload.StepKey,
            payload.Attempt,
            payload.Message,
            payload.Error,
            payload.OccurredAtUtc);

        return Task.FromResult(new AlertDeliveryResult(true));
    }
}
