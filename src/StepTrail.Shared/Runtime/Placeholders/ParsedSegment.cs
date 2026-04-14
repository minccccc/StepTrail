namespace StepTrail.Shared.Runtime.Placeholders;

/// <summary>
/// A segment produced by the placeholder parser.
/// A parsed template is a sequence of alternating literal and placeholder segments.
/// </summary>
public abstract record ParsedSegment;

/// <summary>
/// A literal string segment with no placeholder content.
/// </summary>
public sealed record LiteralSegment(string Value) : ParsedSegment;

/// <summary>
/// A parsed placeholder reference.
///
/// Root identifies which part of the workflow state to navigate:
///   Input   — {{input.field}}
///   Steps   — {{steps.step_name.field}}
///   Secrets — {{secrets.secret_name}}
///
/// Path contains the dot-split segments after the root.
/// For Steps, Path[0] is the step name and Path[1..] is the navigation path.
///
/// Example: {{steps.fetch_order.output.orderId}}
///   Root = Steps
///   Path = ["fetch_order", "output", "orderId"]
/// </summary>
public sealed record PlaceholderSegment(PlaceholderRoot Root, string[] Path) : ParsedSegment
{
    /// <summary>
    /// For Steps placeholders, the name of the referenced step.
    /// </summary>
    public string StepName => Root == PlaceholderRoot.Steps
        ? Path[0]
        : throw new InvalidOperationException("StepName is only available on Steps placeholders.");

    /// <summary>
    /// The navigation path after the root (and after the step name for Steps placeholders).
    /// </summary>
    public string[] NavigationPath => Root == PlaceholderRoot.Steps
        ? Path[1..]
        : Path;

    // Record equality uses reference equality for arrays by default.
    // Override so tests can compare PlaceholderSegment instances by value.
    public bool Equals(PlaceholderSegment? other) =>
        other is not null
        && Root == other.Root
        && Path.SequenceEqual(other.Path);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Root);
        foreach (var segment in Path) hash.Add(segment);
        return hash.ToHashCode();
    }
}
