using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;

namespace StepTrail.Worker;

/// <summary>
/// Resolves {{secrets.name}} placeholders in strings by looking up values in the WorkflowSecrets table.
/// Used by step handlers (e.g. HttpActivityHandler) to inject secrets into URLs, headers, and bodies
/// without ever embedding secret values in the step configuration itself.
///
/// Example: "Bearer {{secrets.stripe-api-key}}" → "Bearer sk_live_abc123"
///
/// If a referenced secret does not exist the placeholder is left unchanged and a warning is logged.
/// </summary>
public sealed class SecretResolver
{
    // Matches {{secrets.any-valid-name}} — letters, digits, hyphens, underscores.
    private static readonly Regex Pattern =
        new(@"\{\{secrets\.([a-zA-Z0-9_\-]+)\}\}", RegexOptions.Compiled);

    private readonly StepTrailDbContext _db;
    private readonly ILogger<SecretResolver> _logger;

    public SecretResolver(StepTrailDbContext db, ILogger<SecretResolver> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Returns the input string with all {{secrets.name}} placeholders replaced.
    /// Returns the input unchanged if it contains no placeholders.
    /// Returns null when input is null.
    /// </summary>
    public async Task<string?> ResolveAsync(string? input, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var matches = Pattern.Matches(input);
        if (matches.Count == 0) return input;

        var names = matches.Select(m => m.Groups[1].Value).Distinct().ToList();

        var secrets = await _db.WorkflowSecrets
            .Where(s => names.Contains(s.Name))
            .ToDictionaryAsync(s => s.Name, s => s.Value, ct);

        return Pattern.Replace(input, match =>
        {
            var name = match.Groups[1].Value;

            if (secrets.TryGetValue(name, out var value))
                return value;

            _logger.LogWarning(
                "Secret '{{secrets.{Name}}}' referenced but not found — placeholder left unresolved",
                name);

            return match.Value;
        });
    }
}
