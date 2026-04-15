namespace StepTrail.Worker.Alerts;

/// <summary>
/// A single alert rule that determines whether a specific runtime condition
/// should generate an alert notification.
/// </summary>
/// <param name="Type">The runtime condition this rule covers.</param>
/// <param name="Enabled">Whether this rule is active. Disabled rules never generate alerts.</param>
public sealed record AlertRule(AlertRuleType Type, bool Enabled = true);
