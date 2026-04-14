namespace StepTrail.Shared.Definitions;

public sealed class ConditionalStepConfiguration
{
    private ConditionalStepConfiguration()
    {
        ConditionExpression = string.Empty;
    }

    public ConditionalStepConfiguration(string conditionExpression)
    {
        if (string.IsNullOrWhiteSpace(conditionExpression))
            throw new ArgumentException("Conditional expression must not be empty.", nameof(conditionExpression));

        ConditionExpression = conditionExpression.Trim();
    }

    public string ConditionExpression { get; private set; }
}
