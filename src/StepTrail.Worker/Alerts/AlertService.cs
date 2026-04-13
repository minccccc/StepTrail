namespace StepTrail.Worker.Alerts;

/// <summary>
/// Fans an alert out to every registered IAlertChannel.
/// Delivery failures are caught and logged — an alert channel failure must never
/// break workflow execution or cause a retry.
/// </summary>
public sealed class AlertService
{
    private readonly IEnumerable<IAlertChannel> _channels;
    private readonly ILogger<AlertService> _logger;

    public AlertService(IEnumerable<IAlertChannel> channels, ILogger<AlertService> logger)
    {
        _channels = channels;
        _logger = logger;
    }

    public async Task SendAsync(AlertPayload payload, CancellationToken ct)
    {
        foreach (var channel in _channels)
        {
            try
            {
                await channel.SendAsync(payload, ct);
            }
            catch (Exception ex)
            {
                // Non-fatal — log and continue to the next channel.
                _logger.LogError(ex,
                    "Alert channel {Channel} failed to deliver [{AlertType}] for instance {InstanceId}",
                    channel.GetType().Name, payload.AlertType, payload.WorkflowInstanceId);
            }
        }
    }
}
