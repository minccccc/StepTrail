using StepTrail.Shared.Definitions;
using Xunit;

namespace StepTrail.Shared.Tests.Definitions;

public class StepDefinitionTests
{
    [Fact]
    public void CreateHttpRequest_CreatesTypedStepDefinition()
    {
        var configuration = new HttpRequestStepConfiguration(
            "https://api.example.com/orders",
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer token" },
            timeoutSeconds: 15);

        var step = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "call-orders-api",
            1,
            configuration,
            retryPolicyOverrideKey: "http-retry-policy");

        Assert.Equal(StepType.HttpRequest, step.Type);
        Assert.Same(configuration, step.HttpRequestConfiguration);
        Assert.Equal(15, step.HttpRequestConfiguration!.TimeoutSeconds);
        Assert.Equal("http-retry-policy", step.RetryPolicyOverrideKey);
        Assert.Null(step.TransformConfiguration);
        Assert.Null(step.ConditionalConfiguration);
        Assert.Null(step.DelayConfiguration);
        Assert.Null(step.SendWebhookConfiguration);
        Assert.Null(step.HttpRequestConfiguration.ResponseClassification);
    }

    [Fact]
    public void CreateTransform_CreatesTypedStepDefinition()
    {
        var configuration = new TransformStepConfiguration(
            [new TransformValueMapping("$.payload.customerId", "$.customer.id")]);

        var step = StepDefinition.CreateTransform(Guid.NewGuid(), "transform-payload", 2, configuration);

        Assert.Equal(StepType.Transform, step.Type);
        Assert.Same(configuration, step.TransformConfiguration);
    }

    [Fact]
    public void CreateTransform_WithOperationMapping_CreatesTypedStepDefinition()
    {
        var configuration = new TransformStepConfiguration(
        [
            new TransformValueMapping(
                "displayName",
                TransformValueOperation.CreateFormatString(
                    "Customer {0}",
                    ["{{input.customerId}}"]))
        ]);

        var step = StepDefinition.CreateTransform(Guid.NewGuid(), "transform-payload", 2, configuration);

        Assert.Equal(StepType.Transform, step.Type);
        Assert.NotNull(step.TransformConfiguration);
        Assert.NotNull(step.TransformConfiguration!.Mappings[0].Operation);
        Assert.Equal(TransformOperationType.FormatString, step.TransformConfiguration.Mappings[0].Operation!.Type);
    }

    [Fact]
    public void CreateConditional_CreatesTypedStepDefinition()
    {
        var configuration = new ConditionalStepConfiguration("payload.status == 'ready'");

        var step = StepDefinition.CreateConditional(Guid.NewGuid(), "check-status", 3, configuration);

        Assert.Equal(StepType.Conditional, step.Type);
        Assert.Same(configuration, step.ConditionalConfiguration);
        Assert.Equal("$.payload.status", step.ConditionalConfiguration!.SourcePath);
        Assert.Equal(ConditionalOperator.Equals, step.ConditionalConfiguration.Operator);
        Assert.Equal("ready", step.ConditionalConfiguration.ExpectedValue);
        Assert.Equal(ConditionalFalseOutcome.CompleteWorkflow, step.ConditionalConfiguration.FalseOutcome);
    }

    [Fact]
    public void CreateConditional_WithExplicitConfiguration_CreatesTypedStepDefinition()
    {
        var configuration = new ConditionalStepConfiguration(
            "{{steps.fetch-order.output.statusCode}}",
            ConditionalOperator.NotEquals,
            "200",
            ConditionalFalseOutcome.CancelWorkflow);

        var step = StepDefinition.CreateConditional(Guid.NewGuid(), "check-status", 3, configuration);

        Assert.Equal(StepType.Conditional, step.Type);
        Assert.Equal("{{steps.fetch-order.output.statusCode}}", step.ConditionalConfiguration!.SourcePath);
        Assert.Equal(ConditionalOperator.NotEquals, step.ConditionalConfiguration.Operator);
        Assert.Equal("200", step.ConditionalConfiguration.ExpectedValue);
        Assert.Equal(ConditionalFalseOutcome.CancelWorkflow, step.ConditionalConfiguration.FalseOutcome);
    }

    [Fact]
    public void CreateDelay_CreatesTypedStepDefinition()
    {
        var configuration = new DelayStepConfiguration(30);

        var step = StepDefinition.CreateDelay(Guid.NewGuid(), "wait-before-retry", 4, configuration);

        Assert.Equal(StepType.Delay, step.Type);
        Assert.Same(configuration, step.DelayConfiguration);
    }

    [Fact]
    public void CreateDelayUntil_CreatesTypedStepDefinition()
    {
        var configuration = new DelayStepConfiguration("{{input.followUpAtUtc}}");

        var step = StepDefinition.CreateDelay(Guid.NewGuid(), "wait-until-follow-up", 4, configuration);

        Assert.Equal(StepType.Delay, step.Type);
        Assert.Same(configuration, step.DelayConfiguration);
        Assert.Null(step.DelayConfiguration!.DelaySeconds);
        Assert.Equal("{{input.followUpAtUtc}}", step.DelayConfiguration.TargetTimeExpression);
    }

    [Fact]
    public void CreateSendWebhook_CreatesTypedStepDefinition()
    {
        var configuration = new SendWebhookStepConfiguration(
            "https://hooks.example.com/outbound",
            headers: new Dictionary<string, string> { ["X-Test"] = "123" },
            timeoutSeconds: 10);

        var step = StepDefinition.CreateSendWebhook(Guid.NewGuid(), "notify-webhook", 5, configuration);

        Assert.Equal(StepType.SendWebhook, step.Type);
        Assert.Same(configuration, step.SendWebhookConfiguration);
        Assert.Equal(10, step.SendWebhookConfiguration!.TimeoutSeconds);
    }

    [Fact]
    public void Constructor_Throws_WhenTypeDoesNotMatchConfiguration()
    {
        var ex = Assert.Throws<ArgumentException>(() => new StepDefinition(
            Guid.NewGuid(),
            "call-orders-api",
            1,
            StepType.HttpRequest,
            delayConfiguration: new DelayStepConfiguration(10)));

        Assert.Equal("type", ex.ParamName);
    }

    [Fact]
    public void Constructor_Throws_WhenMultipleConfigurationsAreProvided()
    {
        var ex = Assert.Throws<ArgumentException>(() => new StepDefinition(
            Guid.NewGuid(),
            "call-orders-api",
            1,
            StepType.HttpRequest,
            httpRequestConfiguration: new HttpRequestStepConfiguration("https://api.example.com/orders"),
            sendWebhookConfiguration: new SendWebhookStepConfiguration("https://hooks.example.com/outbound")));

        Assert.Equal("configuration", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_Throws_WhenKeyIsMissing(string key)
    {
        var ex = Assert.Throws<ArgumentException>(() => StepDefinition.CreateDelay(
            Guid.NewGuid(),
            key,
            1,
            new DelayStepConfiguration(5)));

        Assert.Equal("key", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_Throws_WhenOrderIsInvalid(int order)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => StepDefinition.CreateDelay(
            Guid.NewGuid(),
            "wait",
            order,
            new DelayStepConfiguration(5)));

        Assert.Equal("order", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void HttpRequestConfiguration_Throws_WhenUrlIsMissing(string url)
    {
        var ex = Assert.Throws<ArgumentException>(() => new HttpRequestStepConfiguration(url));

        Assert.Equal("url", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HttpRequestConfiguration_Throws_WhenTimeoutIsInvalid(int timeoutSeconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HttpRequestStepConfiguration(
                "https://api.example.com/orders",
                timeoutSeconds: timeoutSeconds));

        Assert.Equal("timeoutSeconds", ex.ParamName);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(600)]
    public void HttpResponseClassificationConfiguration_Throws_WhenStatusCodeIsOutOfRange(int statusCode)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new HttpResponseClassificationConfiguration(successStatusCodes: [statusCode]));

        Assert.Equal("successStatusCodes", ex.ParamName);
    }

    [Fact]
    public void HttpResponseClassificationConfiguration_Throws_WhenSuccessAndRetryableOverlap()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new HttpResponseClassificationConfiguration(
                successStatusCodes: [200, 409],
                retryableStatusCodes: [409, 503]));

        Assert.Equal("retryableStatusCodes", ex.ParamName);
    }

    [Fact]
    public void TransformConfiguration_Throws_WhenMappingsAreEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() => new TransformStepConfiguration([]));

        Assert.Equal("mappings", ex.ParamName);
    }

    [Fact]
    public void TransformValueOperation_Throws_WhenConcatenatePartsAreEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TransformValueOperation.CreateConcatenate([]));

        Assert.Equal("parts", ex.ParamName);
    }

    [Fact]
    public void TransformValueOperation_Throws_WhenFormatArgumentsAreEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            TransformValueOperation.CreateFormatString("Customer {0}", []));

        Assert.Equal("arguments", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConditionalConfiguration_Throws_WhenExpressionIsMissing(string expression)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ConditionalStepConfiguration(expression));

        Assert.Equal("conditionExpression", ex.ParamName);
    }

    [Fact]
    public void ConditionalConfiguration_Throws_WhenExpectedValueMissing_ForEqualsOperator()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new ConditionalStepConfiguration("$.status", ConditionalOperator.Equals));

        Assert.Equal("expectedValue", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void DelayConfiguration_Throws_WhenDelayIsNotPositive(int delaySeconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new DelayStepConfiguration(delaySeconds));

        Assert.Equal("delaySeconds", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void DelayConfiguration_Throws_WhenTargetTimeExpressionIsEmpty(string targetTimeExpression)
    {
        var ex = Assert.Throws<ArgumentException>(() => new DelayStepConfiguration(targetTimeExpression));

        Assert.Equal("targetTimeExpression", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void SendWebhookConfiguration_Throws_WhenUrlIsMissing(string webhookUrl)
    {
        var ex = Assert.Throws<ArgumentException>(() => new SendWebhookStepConfiguration(webhookUrl));

        Assert.Equal("webhookUrl", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SendWebhookConfiguration_Throws_WhenTimeoutIsInvalid(int timeoutSeconds)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new SendWebhookStepConfiguration(
                "https://hooks.example.com/outbound",
                timeoutSeconds: timeoutSeconds));

        Assert.Equal("timeoutSeconds", ex.ParamName);
    }
}
