namespace StepTrail.Shared.Definitions;

public sealed class ConditionalStepConfiguration
{
    private ConditionalStepConfiguration()
    {
        ConditionExpression = string.Empty;
        SourcePath = string.Empty;
        Operator = ConditionalOperator.Equals;
        ExpectedValue = null;
        FalseOutcome = ConditionalFalseOutcome.CompleteWorkflow;
    }

    public ConditionalStepConfiguration(
        string sourcePath,
        ConditionalOperator @operator,
        string? expectedValue = null,
        ConditionalFalseOutcome falseOutcome = ConditionalFalseOutcome.CompleteWorkflow)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Conditional source path must not be empty.", nameof(sourcePath));

        ValidateOperator(@operator, expectedValue);

        SourcePath = sourcePath.Trim();
        Operator = @operator;
        ExpectedValue = NormalizeExpectedValue(expectedValue);
        FalseOutcome = falseOutcome;
        ConditionExpression = BuildConditionExpression(SourcePath, Operator, ExpectedValue);
    }

    public ConditionalStepConfiguration(
        string conditionExpression,
        ConditionalFalseOutcome falseOutcome = ConditionalFalseOutcome.CompleteWorkflow)
    {
        if (!ConditionalExpressionParser.TryParse(conditionExpression, out var parsed, out var error))
            throw new ArgumentException(error, nameof(conditionExpression));

        SourcePath = parsed!.SourcePath;
        Operator = parsed.Operator;
        ExpectedValue = parsed.ExpectedValue;
        FalseOutcome = falseOutcome;
        ConditionExpression = conditionExpression.Trim();
    }

    public string ConditionExpression { get; private set; }
    public string SourcePath { get; private set; }
    public ConditionalOperator Operator { get; private set; }
    public string? ExpectedValue { get; private set; }
    public ConditionalFalseOutcome FalseOutcome { get; private set; }

    private static void ValidateOperator(ConditionalOperator @operator, string? expectedValue)
    {
        if (!Enum.IsDefined(@operator))
            throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "Conditional operator is not supported.");

        if (@operator is ConditionalOperator.Equals or ConditionalOperator.NotEquals)
        {
            if (string.IsNullOrWhiteSpace(expectedValue))
                throw new ArgumentException("Conditional equals/not-equals operators require an expected value.", nameof(expectedValue));

            return;
        }

        if (expectedValue is not null)
        {
            throw new ArgumentException(
                "Conditional exists/not-exists operators do not accept an expected value.",
                nameof(expectedValue));
        }
    }

    private static string? NormalizeExpectedValue(string? expectedValue) =>
        string.IsNullOrWhiteSpace(expectedValue) ? null : expectedValue.Trim();

    private static string BuildConditionExpression(
        string sourcePath,
        ConditionalOperator @operator,
        string? expectedValue) =>
        @operator switch
        {
            ConditionalOperator.Exists => $"exists({sourcePath})",
            ConditionalOperator.NotExists => $"not exists({sourcePath})",
            ConditionalOperator.Equals => $"{sourcePath} == '{expectedValue}'",
            ConditionalOperator.NotEquals => $"{sourcePath} != '{expectedValue}'",
            _ => throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "Conditional operator is not supported.")
        };
}
