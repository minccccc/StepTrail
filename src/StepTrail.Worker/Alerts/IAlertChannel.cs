namespace StepTrail.Worker.Alerts;

/// <summary>
/// Implemented by each delivery mechanism (console log, webhook, email, …).
/// AlertService fans the payload out to every registered channel.
/// </summary>
public interface IAlertChannel
{
    Task SendAsync(AlertPayload payload, CancellationToken ct);
}
