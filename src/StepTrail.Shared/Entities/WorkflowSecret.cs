namespace StepTrail.Shared.Entities;

/// <summary>
/// Stores a named secret or configuration value that can be referenced from step configs
/// using the placeholder syntax: {{secrets.name}}
/// Values are stored in plaintext. Encrypt the database column or use env-var overrides
/// if encryption at rest is required.
/// </summary>
public class WorkflowSecret
{
    public Guid Id { get; set; }

    /// <summary>
    /// Unique name used in placeholder references. Example: "stripe-api-key".
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The secret value returned when the placeholder is resolved.
    /// </summary>
    public string Value { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
