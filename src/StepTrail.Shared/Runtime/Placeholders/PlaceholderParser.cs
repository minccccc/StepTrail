namespace StepTrail.Shared.Runtime.Placeholders;

/// <summary>
/// Parses template strings containing placeholder references.
///
/// ── Supported syntax ────────────────────────────────────────────────────────
///
///   {{input.fieldName}}
///   {{input.nested.field}}
///   {{steps.step_name.output.fieldName}}
///   {{steps.step_name.output.nested.field}}
///   {{secrets.secret_name}}
///
/// Placeholders are delimited by {{ and }}.
/// Whitespace inside delimiters is trimmed: {{ input.id }} is equivalent to {{input.id}}.
/// Path segments are dot-separated.
/// Valid path segment characters: letters, digits, underscores, hyphens.
///
/// ── Supported roots ─────────────────────────────────────────────────────────
///
///   input   — navigate the workflow's normalized input JSON
///   steps   — navigate a prior step's output; first path segment is the step name
///   secrets — look up a named secret; path is a single segment (the secret name)
///
/// ── Path requirements ───────────────────────────────────────────────────────
///
///   input.*          — at least one path segment required
///   steps.*.*        — at least three path segments required (step name + accessor + field)
///   secrets.*        — exactly one path segment required (secret name)
///
/// ── Not supported in first version ──────────────────────────────────────────
///
///   arithmetic, filters, conditionals, function calls, loops,
///   collection transforms, fallback/default operators, inline formatting DSL
///
/// ── Error behavior ──────────────────────────────────────────────────────────
///
///   Malformed placeholders produce a deterministic parse error.
///   The parser never silently ignores invalid syntax.
///
/// ── Separation of concerns ──────────────────────────────────────────────────
///
///   This parser only detects and tokenizes placeholders.
///   Value resolution against workflow state is handled separately
///   by PlaceholderResolver (PBI-0305).
/// </summary>
public static class PlaceholderParser
{
    private const string Open  = "{{";
    private const string Close = "}}";

    /// <summary>
    /// Parses a template string into an ordered list of literal and placeholder segments.
    /// Returns a failure result for any invalid placeholder syntax.
    /// Returns an empty segment list for null or empty input.
    /// </summary>
    public static PlaceholderParseResult Parse(string? template)
    {
        if (string.IsNullOrEmpty(template))
            return PlaceholderParseResult.Success([]);

        var segments = new List<ParsedSegment>();
        var pos      = 0;

        while (pos < template.Length)
        {
            var openIdx = template.IndexOf(Open, pos, StringComparison.Ordinal);

            if (openIdx < 0)
            {
                // No more placeholders — remainder is a literal.
                segments.Add(new LiteralSegment(template[pos..]));
                break;
            }

            // Literal text before this placeholder.
            if (openIdx > pos)
                segments.Add(new LiteralSegment(template[pos..openIdx]));

            var contentStart = openIdx + Open.Length;
            var closeIdx     = template.IndexOf(Close, contentStart, StringComparison.Ordinal);

            if (closeIdx < 0)
                return PlaceholderParseResult.Failure(
                    $"Unclosed placeholder at position {openIdx}: missing closing '{Close}'.");

            var content = template[contentStart..closeIdx].Trim();

            if (content.Length == 0)
                return PlaceholderParseResult.Failure(
                    $"Empty placeholder at position {openIdx}.");

            var (segment, error) = ParseContent(content);
            if (error is not null)
                return PlaceholderParseResult.Failure(error);

            segments.Add(segment!);
            pos = closeIdx + Close.Length;
        }

        return PlaceholderParseResult.Success(segments);
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private static (PlaceholderSegment? Segment, string? Error) ParseContent(string content)
    {
        var parts = content.Split('.');

        // Validate each path segment before inspecting the root.
        foreach (var part in parts)
        {
            if (part.Length == 0)
                return (null,
                    $"Placeholder '{Display(content)}' contains an empty path segment. " +
                    "Ensure there are no leading, trailing, or consecutive dots.");

            if (!IsValidSegment(part))
                return (null,
                    $"Placeholder segment '{part}' in '{Display(content)}' contains invalid characters. " +
                    "Only letters, digits, underscores, and hyphens are allowed.");
        }

        var rootStr = parts[0];
        var root    = rootStr.ToLowerInvariant() switch
        {
            "input"   => (PlaceholderRoot?)PlaceholderRoot.Input,
            "steps"   => PlaceholderRoot.Steps,
            "secrets" => PlaceholderRoot.Secrets,
            _         => null
        };

        if (root is null)
            return (null,
                $"Unsupported placeholder root '{rootStr}' in '{Display(content)}'. " +
                "Supported roots: input, steps, secrets.");

        var path = parts[1..];

        if (path.Length == 0)
            return (null,
                $"Placeholder '{Display(content)}' has no path after root '{rootStr}'.");

        if (root == PlaceholderRoot.Steps && path.Length < 3)
            return (null,
                $"Placeholder '{Display(content)}' is invalid. " +
                "Steps placeholders require at least three path segments: " +
                "{{steps.<step_name>.output.<field>}}.");

        if (root == PlaceholderRoot.Secrets && path.Length > 1)
            return (null,
                $"Placeholder '{Display(content)}' is invalid. " +
                "Secrets placeholders use a single name segment: {{secrets.<name>}}.");

        return (new PlaceholderSegment(root.Value, path), null);
    }

    private static bool IsValidSegment(string segment) =>
        segment.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-');

    private static string Display(string content) => "{{" + content + "}}";
}
