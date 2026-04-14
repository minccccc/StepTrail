using StepTrail.Shared.Definitions;
using Xunit;

namespace StepTrail.Shared.Tests.Definitions;

public class TriggerDefinitionTests
{
    [Fact]
    public void CreateWebhook_CreatesWebhookTriggerDefinition()
    {
        var configuration = new WebhookTriggerConfiguration("customer-created", "post");

        var trigger = TriggerDefinition.CreateWebhook(Guid.NewGuid(), configuration);

        Assert.Equal(TriggerType.Webhook, trigger.Type);
        Assert.Same(configuration, trigger.WebhookConfiguration);
        Assert.Equal("POST", trigger.WebhookConfiguration!.HttpMethod);
        Assert.Null(trigger.ManualConfiguration);
        Assert.Null(trigger.ApiConfiguration);
        Assert.Null(trigger.ScheduleConfiguration);
    }

    [Fact]
    public void CreateManual_CreatesManualTriggerDefinition()
    {
        var configuration = new ManualTriggerConfiguration("ops-console");

        var trigger = TriggerDefinition.CreateManual(Guid.NewGuid(), configuration);

        Assert.Equal(TriggerType.Manual, trigger.Type);
        Assert.Same(configuration, trigger.ManualConfiguration);
        Assert.Null(trigger.WebhookConfiguration);
        Assert.Null(trigger.ApiConfiguration);
        Assert.Null(trigger.ScheduleConfiguration);
    }

    [Fact]
    public void CreateApi_CreatesApiTriggerDefinition()
    {
        var configuration = new ApiTriggerConfiguration("start-order-fulfillment");

        var trigger = TriggerDefinition.CreateApi(Guid.NewGuid(), configuration);

        Assert.Equal(TriggerType.Api, trigger.Type);
        Assert.Same(configuration, trigger.ApiConfiguration);
        Assert.Null(trigger.WebhookConfiguration);
        Assert.Null(trigger.ManualConfiguration);
        Assert.Null(trigger.ScheduleConfiguration);
    }

    [Fact]
    public void CreateSchedule_CreatesScheduleTriggerDefinition()
    {
        var configuration = new ScheduleTriggerConfiguration(300);

        var trigger = TriggerDefinition.CreateSchedule(Guid.NewGuid(), configuration);

        Assert.Equal(TriggerType.Schedule, trigger.Type);
        Assert.Same(configuration, trigger.ScheduleConfiguration);
        Assert.Null(trigger.WebhookConfiguration);
        Assert.Null(trigger.ManualConfiguration);
        Assert.Null(trigger.ApiConfiguration);
    }

    [Fact]
    public void Constructor_Throws_WhenTypeDoesNotMatchConfiguration()
    {
        var ex = Assert.Throws<ArgumentException>(() => new TriggerDefinition(
            Guid.NewGuid(),
            TriggerType.Webhook,
            manualConfiguration: new ManualTriggerConfiguration("ops-console")));

        Assert.Equal("type", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMultipleConfigurationsAreProvided()
    {
        var ex = Assert.Throws<ArgumentException>(() => new TriggerDefinition(
            Guid.NewGuid(),
            TriggerType.Webhook,
            webhookConfiguration: new WebhookTriggerConfiguration("customer-created"),
            apiConfiguration: new ApiTriggerConfiguration("start-order-fulfillment")));

        Assert.Equal("type", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WebhookConfiguration_Throws_WhenRouteKeyIsMissing(string routeKey)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebhookTriggerConfiguration(routeKey));

        Assert.Equal("routeKey", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ManualConfiguration_Throws_WhenEntryPointKeyIsMissing(string entryPointKey)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ManualTriggerConfiguration(entryPointKey));

        Assert.Equal("entryPointKey", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ApiConfiguration_Throws_WhenOperationKeyIsMissing(string operationKey)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ApiTriggerConfiguration(operationKey));

        Assert.Equal("operationKey", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ScheduleConfiguration_Throws_WhenIntervalIsNotPositive(int intervalSeconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ScheduleTriggerConfiguration(intervalSeconds));

        Assert.Equal("intervalSeconds", ex.ParamName);
    }
}
