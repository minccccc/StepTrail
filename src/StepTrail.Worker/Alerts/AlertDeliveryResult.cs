namespace StepTrail.Worker.Alerts;

/// <summary>
/// Result of a single alert delivery attempt through a channel.
/// </summary>
public sealed record AlertDeliveryResult(bool Success, string? Error = null);
