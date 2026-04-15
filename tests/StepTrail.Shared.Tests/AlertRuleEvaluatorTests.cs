using StepTrail.Worker.Alerts;
using Xunit;

namespace StepTrail.Shared.Tests;

public class AlertRuleEvaluatorTests
{
    [Fact]
    public void DefaultEvaluator_EnablesAllSupportedRuleTypes()
    {
        var evaluator = AlertRuleEvaluator.CreateDefault();

        Assert.True(evaluator.ShouldAlert(AlertRuleType.WorkflowFailed));
        Assert.True(evaluator.ShouldAlert(AlertRuleType.StuckExecutionDetected));
    }

    [Fact]
    public void ShouldAlert_ReturnsFalse_WhenRuleIsDisabled()
    {
        var evaluator = new AlertRuleEvaluator(
        [
            new AlertRule(AlertRuleType.WorkflowFailed, Enabled: false),
            new AlertRule(AlertRuleType.StuckExecutionDetected, Enabled: true)
        ]);

        Assert.False(evaluator.ShouldAlert(AlertRuleType.WorkflowFailed));
        Assert.True(evaluator.ShouldAlert(AlertRuleType.StuckExecutionDetected));
    }

    [Fact]
    public void ShouldAlert_ReturnsFalse_WhenRuleTypeIsNotConfigured()
    {
        var evaluator = new AlertRuleEvaluator(
        [
            new AlertRule(AlertRuleType.WorkflowFailed)
        ]);

        Assert.False(evaluator.ShouldAlert(AlertRuleType.StuckExecutionDetected));
    }

    [Fact]
    public void ShouldAlert_ReturnsFalse_WhenNoRulesConfigured()
    {
        var evaluator = new AlertRuleEvaluator([]);

        Assert.False(evaluator.ShouldAlert(AlertRuleType.WorkflowFailed));
        Assert.False(evaluator.ShouldAlert(AlertRuleType.StuckExecutionDetected));
    }

    [Fact]
    public void Rules_ReturnsAllConfiguredRules()
    {
        var evaluator = AlertRuleEvaluator.CreateDefault();

        Assert.Equal(2, evaluator.Rules.Count);
        Assert.Contains(evaluator.Rules, r => r.Type == AlertRuleType.WorkflowFailed && r.Enabled);
        Assert.Contains(evaluator.Rules, r => r.Type == AlertRuleType.StuckExecutionDetected && r.Enabled);
    }

    [Fact]
    public void AlertRule_DefaultsToEnabled()
    {
        var rule = new AlertRule(AlertRuleType.WorkflowFailed);

        Assert.True(rule.Enabled);
    }

    [Fact]
    public void AlertRule_CanBeExplicitlyDisabled()
    {
        var rule = new AlertRule(AlertRuleType.WorkflowFailed, Enabled: false);

        Assert.False(rule.Enabled);
    }

    [Theory]
    [InlineData(AlertRuleType.WorkflowFailed)]
    [InlineData(AlertRuleType.StuckExecutionDetected)]
    public void IndividualRule_CanBeDisabledWithoutAffectingOthers(AlertRuleType disabledType)
    {
        var rules = Enum.GetValues<AlertRuleType>()
            .Select(t => new AlertRule(t, Enabled: t != disabledType));

        var evaluator = new AlertRuleEvaluator(rules);

        Assert.False(evaluator.ShouldAlert(disabledType));
        foreach (var otherType in Enum.GetValues<AlertRuleType>().Where(t => t != disabledType))
        {
            Assert.True(evaluator.ShouldAlert(otherType));
        }
    }
}
