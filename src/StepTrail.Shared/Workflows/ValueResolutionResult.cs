using System.Text.Json;

namespace StepTrail.Shared.Workflows;

/// <summary>
/// Structured result of resolving a typed value reference against workflow state.
/// Used by step executors that need JSON values rather than string interpolation.
/// </summary>
public sealed class ValueResolutionResult
{
    private ValueResolutionResult(
        JsonElement? value,
        string? error,
        StepExecutionFailureClassification? failureClassification)
    {
        Value = value;
        Error = error;
        FailureClassification = failureClassification;
    }

    public bool IsSuccess => Error is null;
    public JsonElement? Value { get; }
    public string? Error { get; }
    public StepExecutionFailureClassification? FailureClassification { get; }

    public static ValueResolutionResult Success(JsonElement value) =>
        new(value.Clone(), null, null);

    public static ValueResolutionResult InvalidConfiguration(string error) =>
        new(null, error, StepExecutionFailureClassification.InvalidConfiguration);

    public static ValueResolutionResult InputResolutionFailure(string error) =>
        new(null, error, StepExecutionFailureClassification.InputResolutionFailure);
}
