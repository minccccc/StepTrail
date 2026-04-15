using Microsoft.Extensions.DependencyInjection;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Workflows;
using StepTrail.Worker.Handlers;

namespace StepTrail.Worker.StepExecutors;

public static class StepExecutorServiceCollectionExtensions
{
    public static IServiceCollection AddWorkerStepExecutors(this IServiceCollection services)
    {
        services.AddSingleton<IStepExecutorRegistry, StepExecutorRegistry>();
        services.AddSingleton<IHttpResponseClassifier, HttpResponseClassifier>();

        services.AddKeyedScoped<IStepExecutor, HttpActivityHandler>(StepExecutorKeys.HttpRequest);
        services.AddKeyedScoped<IStepExecutor, SendWebhookStepExecutor>(StepExecutorKeys.SendWebhook);
        services.AddKeyedScoped<IStepExecutor, TransformStepExecutor>(StepExecutorKeys.Transform);
        services.AddKeyedScoped<IStepExecutor, ConditionalStepExecutor>(StepExecutorKeys.Conditional);
        services.AddKeyedScoped<IStepExecutor, DelayStepExecutor>(StepExecutorKeys.Delay);

        // Executable step-type registrations for Phase 5+.
        services.AddStepExecutorRegistration(StepType.HttpRequest, StepExecutorKeys.HttpRequest);
        services.AddStepExecutorRegistration(StepType.SendWebhook, StepExecutorKeys.SendWebhook);
        services.AddStepExecutorRegistration(StepType.Transform, StepExecutorKeys.Transform);
        services.AddStepExecutorRegistration(StepType.Conditional, StepExecutorKeys.Conditional);
        services.AddStepExecutorRegistration(StepType.Delay, StepExecutorKeys.Delay);

        return services;
    }

    public static IServiceCollection AddStepExecutorRegistration(
        this IServiceCollection services,
        StepType stepType,
        string executorKey,
        Func<string?, string?>? normalizeConfiguration = null)
    {
        services.AddSingleton(new StepExecutorRegistration(stepType, executorKey, normalizeConfiguration));
        return services;
    }
}
