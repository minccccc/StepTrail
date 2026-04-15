namespace StepTrail.Shared.Entities;

/// <summary>
/// Persisted record of a single alert delivery attempt through a specific channel.
/// One <see cref="AlertRecord"/> can have multiple deliveries (one per channel).
/// </summary>
public class AlertDeliveryRecord
{
    public Guid Id { get; set; }
    public Guid AlertRecordId { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset AttemptedAtUtc { get; set; }
    public string? Error { get; set; }

    public AlertRecord AlertRecord { get; set; } = null!;
}
