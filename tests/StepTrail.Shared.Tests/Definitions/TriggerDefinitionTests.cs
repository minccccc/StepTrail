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
    public void CreateWebhook_CreatesWebhookTriggerDefinition_WithSignatureValidation()
    {
        var signatureValidation = new WebhookSignatureValidationConfiguration(
            "X-StepTrail-Signature",
            "partner-signing-secret",
            WebhookSignatureAlgorithm.HmacSha256,
            "sha256=");
        var configuration = new WebhookTriggerConfiguration("customer-created", "post", signatureValidation);

        var trigger = TriggerDefinition.CreateWebhook(Guid.NewGuid(), configuration);

        Assert.Equal(TriggerType.Webhook, trigger.Type);
        Assert.NotNull(trigger.WebhookConfiguration);
        Assert.NotNull(trigger.WebhookConfiguration!.SignatureValidation);
        Assert.Equal("X-StepTrail-Signature", trigger.WebhookConfiguration.SignatureValidation!.HeaderName);
        Assert.Equal("partner-signing-secret", trigger.WebhookConfiguration.SignatureValidation.SecretName);
        Assert.Equal(WebhookSignatureAlgorithm.HmacSha256, trigger.WebhookConfiguration.SignatureValidation.Algorithm);
        Assert.Equal("sha256=", trigger.WebhookConfiguration.SignatureValidation.SignaturePrefix);
    }

    [Fact]
    public void CreateWebhook_CreatesWebhookTriggerDefinition_WithInputMappings()
    {
        var configuration = new WebhookTriggerConfiguration(
            "customer-created",
            "post",
            inputMappings:
            [
                new WebhookInputMapping("eventId", "body.event_id"),
                new WebhookInputMapping("requestId", "headers.x-request-id")
            ]);

        var trigger = TriggerDefinition.CreateWebhook(Guid.NewGuid(), configuration);

        Assert.Equal(2, trigger.WebhookConfiguration!.InputMappings.Count);
        Assert.Equal("eventId", trigger.WebhookConfiguration.InputMappings[0].TargetPath);
        Assert.Equal("body.event_id", trigger.WebhookConfiguration.InputMappings[0].SourcePath);
        Assert.Equal("requestId", trigger.WebhookConfiguration.InputMappings[1].TargetPath);
        Assert.Equal("headers.x-request-id", trigger.WebhookConfiguration.InputMappings[1].SourcePath);
    }

    [Fact]
    public void CreateWebhook_CreatesWebhookTriggerDefinition_WithIdempotencyKeyExtraction()
    {
        var configuration = new WebhookTriggerConfiguration(
            "customer-created",
            "post",
            idempotencyKeyExtraction: new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id"));

        var trigger = TriggerDefinition.CreateWebhook(Guid.NewGuid(), configuration);

        Assert.NotNull(trigger.WebhookConfiguration);
        Assert.NotNull(trigger.WebhookConfiguration!.IdempotencyKeyExtraction);
        Assert.Equal(
            "headers.x-delivery-id",
            trigger.WebhookConfiguration.IdempotencyKeyExtraction!.SourcePath);
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
    public void CreateSchedule_CreatesScheduleTriggerDefinition_WithCronExpression()
    {
        var configuration = new ScheduleTriggerConfiguration("0 8 * * 1-5");

        var trigger = TriggerDefinition.CreateSchedule(Guid.NewGuid(), configuration);

        Assert.Equal(TriggerType.Schedule, trigger.Type);
        Assert.NotNull(trigger.ScheduleConfiguration);
        Assert.Null(trigger.ScheduleConfiguration!.IntervalSeconds);
        Assert.Equal("0 8 * * 1-5", trigger.ScheduleConfiguration.CronExpression);
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
    public void WebhookSignatureValidationConfiguration_Throws_WhenHeaderNameIsMissing(string headerName)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebhookSignatureValidationConfiguration(
            headerName,
            "partner-signing-secret",
            WebhookSignatureAlgorithm.HmacSha256));

        Assert.Equal("headerName", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WebhookSignatureValidationConfiguration_Throws_WhenSecretNameIsMissing(string secretName)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebhookSignatureValidationConfiguration(
            "X-StepTrail-Signature",
            secretName,
            WebhookSignatureAlgorithm.HmacSha256));

        Assert.Equal("secretName", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WebhookInputMapping_Throws_WhenTargetPathIsMissing(string targetPath)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebhookInputMapping(targetPath, "body.event_id"));

        Assert.Equal("targetPath", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WebhookInputMapping_Throws_WhenSourcePathIsMissing(string sourcePath)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebhookInputMapping("eventId", sourcePath));

        Assert.Equal("sourcePath", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void WebhookIdempotencyKeyExtractionConfiguration_Throws_WhenSourcePathIsMissing(string sourcePath)
    {
        var ex = Assert.Throws<ArgumentException>(() => new WebhookIdempotencyKeyExtractionConfiguration(sourcePath));

        Assert.Equal("sourcePath", ex.ParamName);
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

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("0 8 * *")]
    [InlineData("not a cron")]
    [InlineData("0 8 1 * 1")]
    public void ScheduleConfiguration_Throws_WhenCronExpressionIsInvalid(string cronExpression)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ScheduleTriggerConfiguration(cronExpression));

        Assert.Equal("cronExpression", ex.ParamName);
    }
}
