using StepTrail.Shared.Definitions;

namespace StepTrail.Shared.Runtime.OutputModels;

public sealed class ConditionalStepOutput
{
    public bool Matched { get; init; }
    public string SourcePath { get; init; } = string.Empty;
    public ConditionalOperator Operator { get; init; }
    public string? ActualValue { get; init; }
    public string? ExpectedValue { get; init; }
    public ConditionalFalseOutcome FalseOutcome { get; init; }
}
