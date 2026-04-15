namespace StepTrail.Api.Services;

public sealed class ApiTriggerAuthenticationOptions
{
    public const string SectionName = "ApiTriggerAuthentication";
    public const string DefaultHeaderName = "X-StepTrail-Api-Key";

    /// <summary>
    /// Shared API key used to authenticate API-triggered starts.
    /// When omitted, API trigger authentication remains closed unless AllowUnauthenticated is explicitly enabled.
    /// </summary>
    public string? SharedSecret { get; set; }

    /// <summary>
    /// Explicitly disables API trigger authentication when no shared secret is configured.
    /// Intended only for local development and test scenarios.
    /// </summary>
    public bool AllowUnauthenticated { get; set; }

    /// <summary>
    /// Header that carries the shared API key.
    /// </summary>
    public string HeaderName { get; set; } = DefaultHeaderName;
}
