using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StepTrail.Shared.Workflows;

namespace StepTrail.Shared;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkflowRegistry(this IServiceCollection services)
    {
        services.AddSingleton<IWorkflowRegistry, WorkflowRegistry>();
        return services;
    }

    /// <summary>
    /// Registers a code-first workflow descriptor so it is available in the IWorkflowRegistry.
    /// Call this before AddWorkflowRegistry().
    /// </summary>
    public static IServiceCollection AddWorkflow<TDescriptor>(this IServiceCollection services)
        where TDescriptor : WorkflowDescriptor, new()
    {
        services.AddSingleton<WorkflowDescriptor, TDescriptor>();
        return services;
    }

    public static IServiceCollection AddStepTrailDb(
        this IServiceCollection services,
        IConfiguration configuration,
        string? migrationsAssembly = null)
    {
        var connectionString = configuration.GetConnectionString("StepTrailDb")
            ?? throw new InvalidOperationException("Connection string 'StepTrailDb' is not configured.");

        services.AddDbContext<StepTrailDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                if (migrationsAssembly is not null)
                    npgsql.MigrationsAssembly(migrationsAssembly);
            }));

        return services;
    }
}
