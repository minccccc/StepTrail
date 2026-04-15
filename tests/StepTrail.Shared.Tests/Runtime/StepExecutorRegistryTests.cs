using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;
using StepTrail.Worker.StepExecutors;
using Xunit;

namespace StepTrail.Shared.Tests.Runtime;

public class StepExecutorRegistryTests
{
    [Fact]
    public void AddWorkerStepExecutors_RegistersExecutableStepTypes()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddWorkerStepExecutors();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IStepExecutorRegistry>();

        var httpRequest = registry.Resolve(StepType.HttpRequest, """{"url":"https://api.example.com"}""");
        var sendWebhook = registry.Resolve(StepType.SendWebhook, """{"webhookUrl":"https://hooks.example.com/events"}""");
        var transform = registry.Resolve(StepType.Transform, """{"mappings":[]}""");
        var conditional = registry.Resolve(StepType.Conditional, """{"conditionExpression":"{{input.status}}"}""");
        var delay = registry.Resolve(StepType.Delay, """{"delaySeconds":30}""");

        Assert.Equal(StepExecutorKeys.HttpRequest, httpRequest.ExecutorKey);
        Assert.Equal(StepExecutorKeys.SendWebhook, sendWebhook.ExecutorKey);
        Assert.Equal(StepExecutorKeys.Transform, transform.ExecutorKey);
        Assert.Equal(StepExecutorKeys.Conditional, conditional.ExecutorKey);
        Assert.Equal(StepExecutorKeys.Delay, delay.ExecutorKey);

        var httpExecutor = provider.GetRequiredKeyedService<IStepExecutor>(httpRequest.ExecutorKey);
        var sendWebhookExecutor = provider.GetRequiredKeyedService<IStepExecutor>(sendWebhook.ExecutorKey);
        var transformExecutor = provider.GetRequiredKeyedService<IStepExecutor>(transform.ExecutorKey);
        var conditionalExecutor = provider.GetRequiredKeyedService<IStepExecutor>(conditional.ExecutorKey);
        var delayExecutor = provider.GetRequiredKeyedService<IStepExecutor>(delay.ExecutorKey);
        Assert.IsType<HttpActivityHandler>(httpExecutor);
        Assert.IsType<SendWebhookStepExecutor>(sendWebhookExecutor);
        Assert.IsType<TransformStepExecutor>(transformExecutor);
        Assert.IsType<ConditionalStepExecutor>(conditionalExecutor);
        Assert.IsType<DelayStepExecutor>(delayExecutor);
    }

    [Fact]
    public void Resolve_SendWebhook_UsesDedicatedWebhookExecutor()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpClient();
        services.AddWorkerStepExecutors();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IStepExecutorRegistry>();

        var resolved = registry.Resolve(
            StepType.SendWebhook,
            """
            {
              "webhookUrl": "https://hooks.example.com/events",
              "method": "PUT",
              "headers": { "x-test": "123" },
              "body": "{\"ok\":true}"
            }
            """);

        Assert.Equal(StepExecutorKeys.SendWebhook, resolved.ExecutorKey);
        Assert.NotNull(resolved.StepConfiguration);

        using var document = JsonDocument.Parse(resolved.StepConfiguration!);
        Assert.Equal("https://hooks.example.com/events", document.RootElement.GetProperty("webhookUrl").GetString());
        Assert.Equal("PUT", document.RootElement.GetProperty("method").GetString());
        Assert.Equal("123", document.RootElement.GetProperty("headers").GetProperty("x-test").GetString());
    }

    [Fact]
    public void Resolve_WhenStepTypeIsNotRegistered_ThrowsDeterministicError()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IStepExecutorRegistry, StepExecutorRegistry>();

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<IStepExecutorRegistry>();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            registry.Resolve((StepType)999, null));

        Assert.Contains("999", ex.Message);
        Assert.Contains("No step executor registration exists", ex.Message);
    }

    [Fact]
    public void Resolve_WhenDuplicateRegistrationsExist_ThrowsLoudly()
    {
        var services = new ServiceCollection();
        services.AddStepExecutorRegistration(StepType.HttpRequest, "ExecutorA");
        services.AddStepExecutorRegistration(StepType.HttpRequest, "ExecutorB");
        services.AddSingleton<IStepExecutorRegistry, StepExecutorRegistry>();

        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IStepExecutorRegistry>());

        Assert.Contains("Duplicate step executor registrations", ex.Message);
        Assert.Contains(nameof(StepType.HttpRequest), ex.Message);
    }
}
