namespace StepTrail.Api.Models;

public sealed class UpdateStepRequest
{
    // HttpRequest / SendWebhook shared
    public string? Url { get; init; }
    public string? Method { get; init; }
    public string? Headers { get; init; }
    public string? Body { get; init; }
    public int? TimeoutSeconds { get; init; }

    // Transform (newline-separated "target = source" lines)
    public string? Mappings { get; init; }

    // Conditional
    public string? SourcePath { get; init; }
    public string? Operator { get; init; }
    public string? ExpectedValue { get; init; }
    public string? FalseOutcome { get; init; }

    // Delay
    public int? DelaySeconds { get; init; }
    public string? TargetTimeExpression { get; init; }

    // Retry policy
    public bool EnableRetryPolicy { get; init; }
    public int? RetryMaxAttempts { get; init; }
    public int? RetryInitialDelaySeconds { get; init; }
    public string? RetryBackoffStrategy { get; init; }
    public int? RetryMaxDelaySeconds { get; init; }
    public bool RetryOnTimeout { get; init; } = true;
}
