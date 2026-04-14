using System.Text;
using System.Text.Json;

namespace StepTrail.Shared.Runtime.Placeholders;

/// <summary>
/// Resolves template strings containing placeholder references against workflow runtime state.
///
/// Responsibilities:
///   - parse the template via PlaceholderParser
///   - navigate workflow input JSON for {{input.*}} references
///   - navigate completed step output JSON for {{steps.*.*}} references
///   - look up pre-loaded secret values for {{secrets.*}} references
///   - assemble the final substituted string
///
/// Missing value policy:
///   Any unresolvable reference (missing field, step with no output, unknown secret)
///   returns a failure result. The caller decides how to surface that failure —
///   typically by failing the step execution rather than silently substituting empty string.
///
/// Type policy:
///   Scalar JSON values (string, number, boolean) are converted to their string representation.
///   Null, object, and array values in a string interpolation context are resolution failures.
///
/// Secrets:
///   Callers are responsible for pre-loading all required secrets into the dictionary
///   before calling Resolve. The resolver does not access the secret store directly.
/// </summary>
public sealed class PlaceholderResolver
{
    /// <summary>
    /// Resolves all placeholders in the template string.
    /// </summary>
    /// <param name="template">Template that may contain {{...}} placeholders. Null or empty returns empty string.</param>
    /// <param name="state">Current workflow runtime state.</param>
    /// <param name="secrets">Pre-loaded secrets keyed by name.</param>
    public ResolveResult Resolve(
        string? template,
        WorkflowState state,
        IReadOnlyDictionary<string, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(secrets);

        if (string.IsNullOrEmpty(template))
            return ResolveResult.Success(string.Empty);

        var parseResult = PlaceholderParser.Parse(template);
        if (!parseResult.IsSuccess)
            return ResolveResult.Failure($"Template parse error: {parseResult.Error}");

        if (parseResult.Segments.Count == 0)
            return ResolveResult.Success(string.Empty);

        var sb = new StringBuilder();

        foreach (var segment in parseResult.Segments)
        {
            switch (segment)
            {
                case LiteralSegment literal:
                    sb.Append(literal.Value);
                    break;

                case PlaceholderSegment placeholder:
                    var resolveResult = ResolveSegment(placeholder, state, secrets);
                    if (!resolveResult.IsSuccess)
                        return resolveResult;
                    sb.Append(resolveResult.Value);
                    break;
            }
        }

        return ResolveResult.Success(sb.ToString());
    }

    // ── Segment dispatch ──────────────────────────────────────────────────────

    private static ResolveResult ResolveSegment(
        PlaceholderSegment segment,
        WorkflowState state,
        IReadOnlyDictionary<string, string> secrets) =>
        segment.Root switch
        {
            PlaceholderRoot.Input   => ResolveInput(segment, state),
            PlaceholderRoot.Steps   => ResolveSteps(segment, state),
            PlaceholderRoot.Secrets => ResolveSecret(segment, secrets),
            _                       => ResolveResult.Failure(
                                           $"Unknown placeholder root '{segment.Root}'.")
        };

    // ── Input ─────────────────────────────────────────────────────────────────

    private static ResolveResult ResolveInput(PlaceholderSegment segment, WorkflowState state)
    {
        var display = Display("input", segment.Path);

        if (state.Input is null)
            return ResolveResult.Failure(
                $"Placeholder '{display}': workflow input is null. " +
                "The workflow was started without an input payload.");

        return NavigateJson(state.Input, segment.Path, display);
    }

    // ── Steps ─────────────────────────────────────────────────────────────────
    //
    // Steps placeholders follow the pattern {{steps.<step_name>.<accessor>.<field...>}}.
    //
    // The accessor is NavigationPath[0] and identifies which part of the step state to read.
    // Currently only "output" is supported:
    //
    //   {{steps.fetch-order.output.orderId}}
    //     step state → Output JSON, navigate ["orderId"]
    //
    // Future accessors (status, error) can be added here.

    private static ResolveResult ResolveSteps(PlaceholderSegment segment, WorkflowState state)
    {
        var stepName       = segment.StepName;
        var navigationPath = segment.NavigationPath; // includes accessor as [0]
        var display        = Display("steps", segment.Path);

        if (!state.Steps.TryGetValue(stepName, out var stepState))
            return ResolveResult.Failure(
                $"Placeholder '{display}': step '{stepName}' has no execution data. " +
                "Ensure the step ran before this one and completed successfully.");

        var accessor = navigationPath[0];

        if (accessor != "output")
            return ResolveResult.Failure(
                $"Placeholder '{display}': accessor '{accessor}' is not supported. " +
                "Only 'output' is currently supported: {{steps.<step_name>.output.<field>}}.");

        if (stepState.Output is null)
            return ResolveResult.Failure(
                $"Placeholder '{display}': step '{stepName}' has no output. " +
                "It may not have completed successfully yet.");

        var outputPath = navigationPath[1..]; // path segments inside the output JSON

        if (outputPath.Length == 0)
            return ResolveResult.Failure(
                $"Placeholder '{display}': no field specified after 'output'. " +
                "Use {{steps.<step_name>.output.<field>}} with at least one field segment.");

        return NavigateJson(stepState.Output, outputPath, display);
    }

    // ── Secrets ───────────────────────────────────────────────────────────────

    private static ResolveResult ResolveSecret(
        PlaceholderSegment segment,
        IReadOnlyDictionary<string, string> secrets)
    {
        var name    = segment.Path[0];   // parser enforces exactly one segment
        var display = Display("secrets", segment.Path);

        if (!secrets.TryGetValue(name, out var value))
            return ResolveResult.Failure(
                $"Placeholder '{display}': secret '{name}' was not found. " +
                "Ensure it has been created via the secrets API before the workflow runs.");

        return ResolveResult.Success(value);
    }

    // ── JSON navigation ───────────────────────────────────────────────────────

    private static ResolveResult NavigateJson(string json, string[] path, string display)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            return ResolveResult.Failure(
                $"Placeholder '{display}': failed to parse source JSON — {ex.Message}");
        }

        using (doc)
        {
            var current = doc.RootElement;

            foreach (var segment in path)
            {
                if (current.ValueKind != JsonValueKind.Object)
                    return ResolveResult.Failure(
                        $"Placeholder '{display}': cannot navigate into '{segment}' — " +
                        $"current value is {current.ValueKind}, not an object.");

                if (!current.TryGetProperty(segment, out current))
                    return ResolveResult.Failure(
                        $"Placeholder '{display}': field '{segment}' was not found.");
            }

            return current.ValueKind switch
            {
                JsonValueKind.String  => ResolveResult.Success(current.GetString()!),
                JsonValueKind.Number  => ResolveResult.Success(current.GetRawText()),
                JsonValueKind.True    => ResolveResult.Success("true"),
                JsonValueKind.False   => ResolveResult.Success("false"),
                JsonValueKind.Null    =>
                    ResolveResult.Failure(
                        $"Placeholder '{display}' resolved to null. " +
                        "Null values are not permitted in string interpolation contexts."),
                JsonValueKind.Object  =>
                    ResolveResult.Failure(
                        $"Placeholder '{display}' resolved to an object. " +
                        "Only scalar values (string, number, boolean) are supported in string contexts."),
                JsonValueKind.Array   =>
                    ResolveResult.Failure(
                        $"Placeholder '{display}' resolved to an array. " +
                        "Only scalar values (string, number, boolean) are supported in string contexts."),
                _ =>
                    ResolveResult.Failure(
                        $"Placeholder '{display}' resolved to an unexpected JSON type '{current.ValueKind}'.")
            };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Display(string root, string[] path) =>
        "{{" + root + "." + string.Join(".", path) + "}}";
}
