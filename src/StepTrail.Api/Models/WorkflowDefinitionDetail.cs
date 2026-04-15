using StepTrail.Shared.Definitions;

namespace StepTrail.Api.Models;

public sealed class WorkflowDefinitionDetail
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public int Version { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? SourceTemplateKey { get; init; }
    public int? SourceTemplateVersion { get; init; }
    public bool IsEditable { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; init; }

    public TriggerDetail Trigger { get; init; } = null!;
    public IReadOnlyList<StepDetail> Steps { get; init; } = [];
}

public sealed class TriggerDetail
{
    public string Type { get; init; } = string.Empty;

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

public sealed record StepDetail
{
    public Guid Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public int Order { get; init; }
    public string? RetryPolicyOverrideKey { get; init; }

    // HttpRequest / SendWebhook shared
    public string? Url { get; init; }
    public string? Method { get; init; }
    public string? Headers { get; init; }
    public string? Body { get; init; }
    public int? TimeoutSeconds { get; init; }

    // Transform
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
    public bool HasRetryPolicy { get; init; }
    public int? RetryMaxAttempts { get; init; }
    public int? RetryInitialDelaySeconds { get; init; }
    public string? RetryBackoffStrategy { get; init; }
    public int? RetryMaxDelaySeconds { get; init; }
    public bool? RetryOnTimeout { get; init; }
}

public static class WorkflowDefinitionDetailMapper
{
    public static WorkflowDefinitionDetail Map(WorkflowDefinition definition) =>
        new()
        {
            Id = definition.Id,
            Key = definition.Key,
            Name = definition.Name,
            Version = definition.Version,
            Status = definition.Status.ToString(),
            Description = definition.Description,
            SourceTemplateKey = definition.SourceTemplateKey,
            SourceTemplateVersion = definition.SourceTemplateVersion,
            IsEditable = definition.Status is WorkflowDefinitionStatus.Draft or WorkflowDefinitionStatus.Inactive,
            CreatedAtUtc = definition.CreatedAtUtc,
            UpdatedAtUtc = definition.UpdatedAtUtc,
            Trigger = MapTrigger(definition.TriggerDefinition),
            Steps = definition.StepDefinitions
                .OrderBy(s => s.Order)
                .Select(MapStep)
                .ToList()
        };

    private static TriggerDetail MapTrigger(TriggerDefinition trigger)
    {
        var detail = new TriggerDetail { Type = trigger.Type.ToString() };

        return trigger.Type switch
        {
            TriggerType.Webhook => new TriggerDetail
            {
                Type = trigger.Type.ToString(),
                RouteKey = trigger.WebhookConfiguration!.RouteKey,
                HttpMethod = trigger.WebhookConfiguration.HttpMethod,
                SignatureHeaderName = trigger.WebhookConfiguration.SignatureValidation?.HeaderName,
                SignatureSecretName = trigger.WebhookConfiguration.SignatureValidation?.SecretName,
                SignatureAlgorithm = trigger.WebhookConfiguration.SignatureValidation?.Algorithm.ToString(),
                SignaturePrefix = trigger.WebhookConfiguration.SignatureValidation?.SignaturePrefix,
                IdempotencyKeySourcePath = trigger.WebhookConfiguration.IdempotencyKeyExtraction?.SourcePath
            },
            TriggerType.Manual => new TriggerDetail
            {
                Type = trigger.Type.ToString(),
                EntryPointKey = trigger.ManualConfiguration!.EntryPointKey
            },
            TriggerType.Api => new TriggerDetail
            {
                Type = trigger.Type.ToString(),
                OperationKey = trigger.ApiConfiguration!.OperationKey
            },
            TriggerType.Schedule => new TriggerDetail
            {
                Type = trigger.Type.ToString(),
                IntervalSeconds = trigger.ScheduleConfiguration!.IntervalSeconds,
                CronExpression = trigger.ScheduleConfiguration.CronExpression
            },
            _ => detail
        };
    }

    private static StepDetail MapStep(StepDefinition step)
    {
        var detail = new StepDetail
        {
            Id = step.Id,
            Key = step.Key,
            Type = step.Type.ToString(),
            Order = step.Order,
            RetryPolicyOverrideKey = step.RetryPolicyOverrideKey,
            HasRetryPolicy = step.RetryPolicy is not null,
            RetryMaxAttempts = step.RetryPolicy?.MaxAttempts,
            RetryInitialDelaySeconds = step.RetryPolicy?.InitialDelaySeconds,
            RetryBackoffStrategy = step.RetryPolicy?.BackoffStrategy.ToString(),
            RetryMaxDelaySeconds = step.RetryPolicy?.MaxDelaySeconds,
            RetryOnTimeout = step.RetryPolicy?.RetryOnTimeout
        };

        return step.Type switch
        {
            StepType.HttpRequest when step.HttpRequestConfiguration is not null => detail with
            {
                Url = step.HttpRequestConfiguration.Url,
                Method = step.HttpRequestConfiguration.Method,
                Headers = step.HttpRequestConfiguration.Headers.Count > 0
                    ? string.Join("\n", step.HttpRequestConfiguration.Headers.Select(h => $"{h.Key}: {h.Value}"))
                    : null,
                Body = step.HttpRequestConfiguration.Body,
                TimeoutSeconds = step.HttpRequestConfiguration.TimeoutSeconds
            },
            StepType.SendWebhook when step.SendWebhookConfiguration is not null => detail with
            {
                Url = step.SendWebhookConfiguration.WebhookUrl,
                Method = step.SendWebhookConfiguration.Method,
                Headers = step.SendWebhookConfiguration.Headers.Count > 0
                    ? string.Join("\n", step.SendWebhookConfiguration.Headers.Select(h => $"{h.Key}: {h.Value}"))
                    : null,
                Body = step.SendWebhookConfiguration.Body,
                TimeoutSeconds = step.SendWebhookConfiguration.TimeoutSeconds
            },
            StepType.Transform when step.TransformConfiguration is not null => detail with
            {
                Mappings = string.Join("\n", step.TransformConfiguration.Mappings
                    .Select(FormatTransformMapping))
            },
            StepType.Conditional when step.ConditionalConfiguration is not null => detail with
            {
                SourcePath = step.ConditionalConfiguration.SourcePath,
                Operator = step.ConditionalConfiguration.Operator.ToString(),
                ExpectedValue = step.ConditionalConfiguration.ExpectedValue,
                FalseOutcome = step.ConditionalConfiguration.FalseOutcome.ToString()
            },
            StepType.Delay when step.DelayConfiguration is not null => detail with
            {
                DelaySeconds = step.DelayConfiguration.DelaySeconds,
                TargetTimeExpression = step.DelayConfiguration.TargetTimeExpression
            },
            _ => detail
        };
    }

    private static string FormatTransformMapping(TransformValueMapping m)
    {
        if (m.Operation is null)
            return $"{m.NormalizedTargetPath} = {m.SourcePath}";

        return m.Operation.Type switch
        {
            TransformOperationType.DefaultValue =>
                $"{m.NormalizedTargetPath} = default({m.Operation.SourcePath}, \"{m.Operation.DefaultValue}\")",
            TransformOperationType.Concatenate =>
                $"{m.NormalizedTargetPath} = concat({string.Join(", ", m.Operation.Parts)})",
            TransformOperationType.FormatString =>
                $"{m.NormalizedTargetPath} = format(\"{m.Operation.Template}\", {string.Join(", ", m.Operation.Arguments)})",
            _ => $"{m.NormalizedTargetPath} = (unsupported operation)"
        };
    }
}
