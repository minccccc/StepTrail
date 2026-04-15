namespace StepTrail.Shared.Definitions;

public sealed class StepDefinition
{
    private StepDefinition()
    {
        Key = string.Empty;
        HttpRequestConfiguration = null!;
        TransformConfiguration = null!;
        ConditionalConfiguration = null!;
        DelayConfiguration = null!;
        SendWebhookConfiguration = null!;
    }

    public StepDefinition(
        Guid id,
        string key,
        int order,
        StepType type,
        HttpRequestStepConfiguration? httpRequestConfiguration = null,
        TransformStepConfiguration? transformConfiguration = null,
        ConditionalStepConfiguration? conditionalConfiguration = null,
        DelayStepConfiguration? delayConfiguration = null,
        SendWebhookStepConfiguration? sendWebhookConfiguration = null,
        string? retryPolicyOverrideKey = null,
        RetryPolicy? retryPolicy = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Step definition id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Step definition key must not be empty.", nameof(key));
        if (order < 1)
            throw new ArgumentOutOfRangeException(nameof(order), "Step definition order must be 1 or greater.");

        Id = id;
        Key = key.Trim();
        Order = order;
        Type = type;
        HttpRequestConfiguration = httpRequestConfiguration;
        TransformConfiguration = transformConfiguration;
        ConditionalConfiguration = conditionalConfiguration;
        DelayConfiguration = delayConfiguration;
        SendWebhookConfiguration = sendWebhookConfiguration;
        RetryPolicyOverrideKey = string.IsNullOrWhiteSpace(retryPolicyOverrideKey)
            ? null
            : retryPolicyOverrideKey.Trim();
        RetryPolicy = retryPolicy;

        ValidateConfiguration(type, httpRequestConfiguration, transformConfiguration, conditionalConfiguration, delayConfiguration, sendWebhookConfiguration);
    }

    public Guid Id { get; private set; }
    public string Key { get; private set; }
    public int Order { get; private set; }
    public StepType Type { get; private set; }
    public string? RetryPolicyOverrideKey { get; private set; }

    /// <summary>
    /// Optional per-step retry policy. When null, the global default applies.
    /// </summary>
    public RetryPolicy? RetryPolicy { get; private set; }

    public HttpRequestStepConfiguration? HttpRequestConfiguration { get; private set; }
    public TransformStepConfiguration? TransformConfiguration { get; private set; }
    public ConditionalStepConfiguration? ConditionalConfiguration { get; private set; }
    public DelayStepConfiguration? DelayConfiguration { get; private set; }
    public SendWebhookStepConfiguration? SendWebhookConfiguration { get; private set; }

    public static StepDefinition CreateHttpRequest(
        Guid id,
        string key,
        int order,
        HttpRequestStepConfiguration configuration,
        string? retryPolicyOverrideKey = null,
        RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new StepDefinition(
            id,
            key,
            order,
            StepType.HttpRequest,
            httpRequestConfiguration: configuration,
            retryPolicyOverrideKey: retryPolicyOverrideKey,
            retryPolicy: retryPolicy);
    }

    public static StepDefinition CreateTransform(
        Guid id,
        string key,
        int order,
        TransformStepConfiguration configuration,
        string? retryPolicyOverrideKey = null,
        RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new StepDefinition(
            id,
            key,
            order,
            StepType.Transform,
            transformConfiguration: configuration,
            retryPolicyOverrideKey: retryPolicyOverrideKey,
            retryPolicy: retryPolicy);
    }

    public static StepDefinition CreateConditional(
        Guid id,
        string key,
        int order,
        ConditionalStepConfiguration configuration,
        string? retryPolicyOverrideKey = null,
        RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new StepDefinition(
            id,
            key,
            order,
            StepType.Conditional,
            conditionalConfiguration: configuration,
            retryPolicyOverrideKey: retryPolicyOverrideKey,
            retryPolicy: retryPolicy);
    }

    public static StepDefinition CreateDelay(
        Guid id,
        string key,
        int order,
        DelayStepConfiguration configuration,
        string? retryPolicyOverrideKey = null,
        RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new StepDefinition(
            id,
            key,
            order,
            StepType.Delay,
            delayConfiguration: configuration,
            retryPolicyOverrideKey: retryPolicyOverrideKey,
            retryPolicy: retryPolicy);
    }

    public static StepDefinition CreateSendWebhook(
        Guid id,
        string key,
        int order,
        SendWebhookStepConfiguration configuration,
        string? retryPolicyOverrideKey = null,
        RetryPolicy? retryPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new StepDefinition(
            id,
            key,
            order,
            StepType.SendWebhook,
            sendWebhookConfiguration: configuration,
            retryPolicyOverrideKey: retryPolicyOverrideKey,
            retryPolicy: retryPolicy);
    }

    private static void ValidateConfiguration(
        StepType type,
        HttpRequestStepConfiguration? httpRequestConfiguration,
        TransformStepConfiguration? transformConfiguration,
        ConditionalStepConfiguration? conditionalConfiguration,
        DelayStepConfiguration? delayConfiguration,
        SendWebhookStepConfiguration? sendWebhookConfiguration)
    {
        var configuredCount =
            (httpRequestConfiguration is null ? 0 : 1) +
            (transformConfiguration is null ? 0 : 1) +
            (conditionalConfiguration is null ? 0 : 1) +
            (delayConfiguration is null ? 0 : 1) +
            (sendWebhookConfiguration is null ? 0 : 1);

        if (configuredCount != 1)
            throw new ArgumentException(
                "Step definition must contain exactly one type-specific configuration.",
                "configuration");

        var hasMatchingConfiguration = type switch
        {
            StepType.HttpRequest => httpRequestConfiguration is not null,
            StepType.Transform => transformConfiguration is not null,
            StepType.Conditional => conditionalConfiguration is not null,
            StepType.Delay => delayConfiguration is not null,
            StepType.SendWebhook => sendWebhookConfiguration is not null,
            _ => false
        };

        if (!hasMatchingConfiguration)
            throw new ArgumentException(
                $"Step type '{type}' does not match the supplied configuration payload.",
                nameof(type));
    }
}
