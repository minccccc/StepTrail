namespace StepTrail.Worker.Alerts;

/// <summary>
/// Implemented by each delivery mechanism (console log, webhook, email, ...).
/// AlertService fans the payload out to every registered channel.
/// </summary>
public interface IAlertChannel
{
    /// <summary>
    /// The channel name used for persistence and display (e.g. "ConsoleLog", "Webhook").
    /// </summary>
    string ChannelName { get; }

    /// <summary>
    /// Delivers the alert and returns the delivery outcome.
    /// </summary>
    Task<AlertDeliveryResult> SendAsync(AlertPayload payload, CancellationToken ct);
}
