using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Worker.Alerts;

/// <summary>
/// Fans an alert out to every registered IAlertChannel, persists the alert record
/// and each delivery attempt result. Delivery failures are caught and logged —
/// an alert channel failure must never break workflow execution or cause a retry.
/// </summary>
public sealed class AlertService
{
    private readonly IEnumerable<IAlertChannel> _channels;
    private readonly StepTrailDbContext _db;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        IEnumerable<IAlertChannel> channels,
        StepTrailDbContext db,
        ILogger<AlertService> logger)
    {
        _channels = channels;
        _db = db;
        _logger = logger;
    }

    public async Task SendAsync(AlertPayload payload, CancellationToken ct)
    {
        var alertRecord = new AlertRecord
        {
            Id = Guid.NewGuid(),
            AlertType = payload.AlertType,
            WorkflowInstanceId = payload.WorkflowInstanceId,
            WorkflowKey = payload.WorkflowKey,
            StepKey = payload.StepKey,
            Attempt = payload.Attempt,
            Cause = payload.Error,
            GeneratedAtUtc = payload.OccurredAtUtc
        };

        _db.AlertRecords.Add(alertRecord);

        foreach (var channel in _channels)
        {
            var now = DateTimeOffset.UtcNow;
            AlertDeliveryResult result;

            try
            {
                result = await channel.SendAsync(payload, ct);
            }
            catch (Exception ex)
            {
                // Non-fatal — log and continue to the next channel.
                _logger.LogError(ex,
                    "Alert channel {Channel} failed to deliver [{AlertType}] for instance {InstanceId}",
                    channel.ChannelName, payload.AlertType, payload.WorkflowInstanceId);
                result = new AlertDeliveryResult(false, ex.Message);
            }

            _db.AlertDeliveryRecords.Add(new AlertDeliveryRecord
            {
                Id = Guid.NewGuid(),
                AlertRecordId = alertRecord.Id,
                Channel = channel.ChannelName,
                Status = result.Success ? "Delivered" : "Failed",
                AttemptedAtUtc = now,
                Error = result.Error
            });
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Alert history persistence is best-effort — never block the caller.
            _logger.LogError(ex, "Failed to persist alert history for [{AlertType}] instance {InstanceId}",
                payload.AlertType, payload.WorkflowInstanceId);
        }
    }
}
