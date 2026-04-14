namespace StepTrail.Shared.Runtime.Placeholders;

/// <summary>
/// Result of resolving a template string against workflow state.
/// On success, Value contains the fully substituted string.
/// On failure, Error contains a human-readable description of which placeholder failed and why.
/// </summary>
public sealed class ResolveResult
{
    private ResolveResult(string? value, string? error)
    {
        Value = value;
        Error = error;
    }

    public bool IsSuccess => Error is null;

    /// <summary>Fully substituted string. Only valid when IsSuccess is true.</summary>
    public string? Value { get; }

    /// <summary>Human-readable error. Null when IsSuccess is true.</summary>
    public string? Error { get; }

    public static ResolveResult Success(string value) => new(value, null);
    public static ResolveResult Failure(string error) => new(null, error);
}
