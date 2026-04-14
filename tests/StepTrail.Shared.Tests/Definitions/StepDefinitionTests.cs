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
            headers: new Dictionary<string, string> { ["Authorization"] = "Bearer token" });

        var step = StepDefinition.CreateHttpRequest(
            Guid.NewGuid(),
            "call-orders-api",
            1,
            configuration,
            retryPolicyOverrideKey: "http-retry-policy");

        Assert.Equal(StepType.HttpRequest, step.Type);
        Assert.Same(configuration, step.HttpRequestConfiguration);
        Assert.Equal("http-retry-policy", step.RetryPolicyOverrideKey);
        Assert.Null(step.TransformConfiguration);
        Assert.Null(step.ConditionalConfiguration);
        Assert.Null(step.DelayConfiguration);
        Assert.Null(step.SendWebhookConfiguration);
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
    public void CreateConditional_CreatesTypedStepDefinition()
    {
        var configuration = new ConditionalStepConfiguration("payload.status == 'ready'");

        var step = StepDefinition.CreateConditional(Guid.NewGuid(), "check-status", 3, configuration);

        Assert.Equal(StepType.Conditional, step.Type);
        Assert.Same(configuration, step.ConditionalConfiguration);
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
    public void CreateSendWebhook_CreatesTypedStepDefinition()
    {
        var configuration = new SendWebhookStepConfiguration("https://hooks.example.com/outbound");

        var step = StepDefinition.CreateSendWebhook(Guid.NewGuid(), "notify-webhook", 5, configuration);

        Assert.Equal(StepType.SendWebhook, step.Type);
        Assert.Same(configuration, step.SendWebhookConfiguration);
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

        Assert.Equal("type", ex.ParamName);
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

    [Fact]
    public void TransformConfiguration_Throws_WhenMappingsAreEmpty()
    {
        var ex = Assert.Throws<ArgumentException>(() => new TransformStepConfiguration([]));

        Assert.Equal("mappings", ex.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ConditionalConfiguration_Throws_WhenExpressionIsMissing(string expression)
    {
        var ex = Assert.Throws<ArgumentException>(() => new ConditionalStepConfiguration(expression));

        Assert.Equal("conditionExpression", ex.ParamName);
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
    public void SendWebhookConfiguration_Throws_WhenUrlIsMissing(string webhookUrl)
    {
        var ex = Assert.Throws<ArgumentException>(() => new SendWebhookStepConfiguration(webhookUrl));

        Assert.Equal("webhookUrl", ex.ParamName);
    }
}
