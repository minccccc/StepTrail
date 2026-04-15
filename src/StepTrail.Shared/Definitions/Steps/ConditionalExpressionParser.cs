namespace StepTrail.Shared.Definitions;

public static class ConditionalExpressionParser
{
    public static bool TryParse(
        string? expression,
        out ParsedConditionalExpression? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Conditional expression must not be empty.";
            return false;
        }

        var trimmed = expression.Trim();

        if (TryParseExists(trimmed, negated: false, out parsed, out error)
            || TryParseExists(trimmed, negated: true, out parsed, out error)
            || TryParseBinary(trimmed, out parsed, out error))
        {
            return error is null;
        }

        error ??= "Conditional expression must use one of: <source> == <value>, <source> != <value>, exists(<source>), or not exists(<source>).";
        return false;
    }

    private static bool TryParseExists(
        string expression,
        bool negated,
        out ParsedConditionalExpression? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        var prefix = negated ? "not exists(" : "exists(";
        if (!expression.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !expression.EndsWith(')'))
        {
            return false;
        }

        var operand = expression[prefix.Length..^1].Trim();
        if (string.IsNullOrWhiteSpace(operand))
        {
            error = "Conditional exists operator requires a source path.";
            return true;
        }

        parsed = new ParsedConditionalExpression(
            NormalizeSourcePath(operand),
            negated ? ConditionalOperator.NotExists : ConditionalOperator.Exists,
            null);
        return true;
    }

    private static bool TryParseBinary(
        string expression,
        out ParsedConditionalExpression? parsed,
        out string? error)
    {
        parsed = null;
        error = null;

        var operatorToken = expression.Contains("!=", StringComparison.Ordinal)
            ? "!="
            : expression.Contains("==", StringComparison.Ordinal)
                ? "=="
                : null;

        if (operatorToken is null)
            return false;

        var parts = expression.Split([operatorToken], 2, StringSplitOptions.None);
        if (parts.Length != 2)
        {
            error = "Conditional binary expression is malformed.";
            return true;
        }

        var left = parts[0].Trim();
        var right = parts[1].Trim();

        if (string.IsNullOrWhiteSpace(left))
        {
            error = "Conditional expression requires a source path on the left side.";
            return true;
        }

        if (string.IsNullOrWhiteSpace(right))
        {
            error = "Conditional expression requires an expected value on the right side.";
            return true;
        }

        parsed = new ParsedConditionalExpression(
            NormalizeSourcePath(left),
            operatorToken == "==" ? ConditionalOperator.Equals : ConditionalOperator.NotEquals,
            NormalizeExpectedValue(right));
        return true;
    }

    private static string NormalizeSourcePath(string operand)
    {
        var trimmed = operand.Trim();

        if (trimmed.StartsWith("{{", StringComparison.Ordinal) && trimmed.EndsWith("}}", StringComparison.Ordinal))
            return trimmed;

        if (trimmed == "$" || trimmed.StartsWith("$.", StringComparison.Ordinal))
            return trimmed;

        if (trimmed.StartsWith("input.", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("steps.", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("secrets.", StringComparison.OrdinalIgnoreCase))
        {
            return "{{" + trimmed + "}}";
        }

        return trimmed.StartsWith(".", StringComparison.Ordinal)
            ? "$" + trimmed
            : "$." + trimmed;
    }

    private static string NormalizeExpectedValue(string value)
    {
        var trimmed = value.Trim();

        if ((trimmed.StartsWith('\'') && trimmed.EndsWith('\''))
            || (trimmed.StartsWith('"') && trimmed.EndsWith('"')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    public sealed record ParsedConditionalExpression(
        string SourcePath,
        ConditionalOperator Operator,
        string? ExpectedValue);
}
