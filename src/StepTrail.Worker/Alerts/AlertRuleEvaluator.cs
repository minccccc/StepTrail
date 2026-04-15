namespace StepTrail.Worker.Alerts;

/// <summary>
/// Evaluates whether a runtime event should generate an alert based on the
/// configured set of alert rules. Injected into services that need to make
/// alert decisions (e.g. <see cref="StepFailureService"/>).
///
/// The default rule set enables alerts for all supported conditions.
/// Rules can be disabled via configuration or by supplying a custom set.
/// </summary>
public sealed class AlertRuleEvaluator
{
    private readonly Dictionary<AlertRuleType, AlertRule> _rules;

    public AlertRuleEvaluator(IEnumerable<AlertRule> rules)
    {
        _rules = rules.ToDictionary(r => r.Type);
    }

    /// <summary>
    /// Returns true if an alert should be generated for the given rule type.
    /// Returns false if the rule type is unknown or disabled.
    /// </summary>
    public bool ShouldAlert(AlertRuleType type) =>
        _rules.TryGetValue(type, out var rule) && rule.Enabled;

    /// <summary>
    /// Returns all configured rules for inspection/debugging.
    /// </summary>
    public IReadOnlyCollection<AlertRule> Rules => _rules.Values;

    /// <summary>
    /// Creates the default evaluator with all supported conditions enabled.
    /// </summary>
    public static AlertRuleEvaluator CreateDefault() => new(
    [
        new AlertRule(AlertRuleType.WorkflowFailed),
        new AlertRule(AlertRuleType.StuckExecutionDetected)
    ]);
}
