namespace StepTrail.Api.Models;

public sealed class UpsertSecretRequest
{
    /// <summary>
    /// The secret value to store. Required.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// Optional human-readable description of what this secret is used for.
    /// </summary>
    public string? Description { get; set; }
}
