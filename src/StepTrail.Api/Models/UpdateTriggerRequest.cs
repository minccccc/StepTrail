namespace StepTrail.Api.Models;

public sealed class UpdateTriggerRequest
{
    // Webhook
    public string? RouteKey { get; init; }
    public string? HttpMethod { get; init; }
    public string? SignatureHeaderName { get; init; }
    public string? SignatureSecretName { get; init; }
    public string? SignatureAlgorithm { get; init; }
    public string? SignaturePrefix { get; init; }
    public string? IdempotencyKeySourcePath { get; init; }

    // Manual
    public string? EntryPointKey { get; init; }

    // Api
    public string? OperationKey { get; init; }

    // Schedule
    public int? IntervalSeconds { get; init; }
    public string? CronExpression { get; init; }
}
