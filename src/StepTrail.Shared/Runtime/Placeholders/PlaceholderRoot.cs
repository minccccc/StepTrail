namespace StepTrail.Shared.Runtime.Placeholders;

/// <summary>
/// The supported root identifiers in the placeholder syntax.
///
/// First-version supported roots:
///   input   — resolves against the normalized workflow input
///   steps   — resolves against the output of a named prior step
///   secrets — resolves against the named secret store
///
/// No other roots are supported. Unrecognized roots produce a parse error.
/// </summary>
public enum PlaceholderRoot
{
    Input,
    Steps,
    Secrets
}
