namespace StepTrail.Shared.Definitions;

public sealed class TriggerDefinition
{
    private TriggerDefinition()
    {
        WebhookConfiguration = null!;
        ManualConfiguration = null!;
        ScheduleConfiguration = null!;
    }

    public TriggerDefinition(
        Guid id,
        TriggerType type,
        WebhookTriggerConfiguration? webhookConfiguration = null,
        ManualTriggerConfiguration? manualConfiguration = null,
        ScheduleTriggerConfiguration? scheduleConfiguration = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Trigger definition id must not be empty.", nameof(id));

        Id = id;
        Type = type;
        WebhookConfiguration = webhookConfiguration;
        ManualConfiguration = manualConfiguration;
        ScheduleConfiguration = scheduleConfiguration;

        ValidateConfiguration(type, webhookConfiguration, manualConfiguration, scheduleConfiguration);
    }

    public Guid Id { get; private set; }
    public TriggerType Type { get; private set; }
    public WebhookTriggerConfiguration? WebhookConfiguration { get; private set; }
    public ManualTriggerConfiguration? ManualConfiguration { get; private set; }
    public ScheduleTriggerConfiguration? ScheduleConfiguration { get; private set; }

    public static TriggerDefinition CreateWebhook(Guid id, WebhookTriggerConfiguration configuration) =>
        new(id, TriggerType.Webhook, webhookConfiguration: configuration);

    public static TriggerDefinition CreateManual(Guid id, ManualTriggerConfiguration configuration) =>
        new(id, TriggerType.Manual, manualConfiguration: configuration);

    public static TriggerDefinition CreateSchedule(Guid id, ScheduleTriggerConfiguration configuration) =>
        new(id, TriggerType.Schedule, scheduleConfiguration: configuration);

    private static void ValidateConfiguration(
        TriggerType type,
        WebhookTriggerConfiguration? webhookConfiguration,
        ManualTriggerConfiguration? manualConfiguration,
        ScheduleTriggerConfiguration? scheduleConfiguration)
    {
        var configuredCount =
            (webhookConfiguration is null ? 0 : 1) +
            (manualConfiguration is null ? 0 : 1) +
            (scheduleConfiguration is null ? 0 : 1);

        if (configuredCount != 1)
            throw new ArgumentException(
                "Trigger definition must contain exactly one type-specific configuration.",
                nameof(type));

        var hasMatchingConfiguration = type switch
        {
            TriggerType.Webhook => webhookConfiguration is not null,
            TriggerType.Manual => manualConfiguration is not null,
            TriggerType.Schedule => scheduleConfiguration is not null,
            _ => false
        };

        if (!hasMatchingConfiguration)
            throw new ArgumentException(
                $"Trigger type '{type}' does not match the supplied configuration payload.",
                nameof(type));
    }
}
