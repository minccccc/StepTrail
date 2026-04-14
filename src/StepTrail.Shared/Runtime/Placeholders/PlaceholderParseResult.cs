namespace StepTrail.Shared.Runtime.Placeholders;

/// <summary>
/// Result of parsing a template string.
/// On success, Segments contains the ordered literal and placeholder tokens.
/// On failure, Error contains a human-readable message describing the problem.
/// </summary>
public sealed class PlaceholderParseResult
{
    private PlaceholderParseResult(
        IReadOnlyList<ParsedSegment> segments,
        string? error)
    {
        Segments = segments;
        Error    = error;
    }

    public bool IsSuccess => Error is null;

    /// <summary>
    /// Ordered list of literal and placeholder segments.
    /// Empty when the template is null or empty.
    /// Only valid when IsSuccess is true.
    /// </summary>
    public IReadOnlyList<ParsedSegment> Segments { get; }

    /// <summary>Human-readable error. Null when IsSuccess is true.</summary>
    public string? Error { get; }

    public static PlaceholderParseResult Success(IReadOnlyList<ParsedSegment> segments) =>
        new(segments, null);

    public static PlaceholderParseResult Failure(string error) =>
        new([], error);
}
