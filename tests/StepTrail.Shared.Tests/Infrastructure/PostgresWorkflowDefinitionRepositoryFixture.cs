using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;

namespace StepTrail.Shared.Tests.Infrastructure;

public sealed class PostgresWorkflowDefinitionRepositoryFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("steptrail_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public StepTrailDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<StepTrailDbContext>()
            .UseNpgsql(
                _container.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly("StepTrail.Api"))
            .Options;

        return new StepTrailDbContext(options);
    }

    public async Task ResetAsync()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE
                workflow_events,
                workflow_step_executions,
                idempotency_records,
                recurring_workflow_schedules,
                workflow_instances,
                executable_step_definitions,
                executable_trigger_definitions,
                executable_workflow_definitions,
                workflow_definition_steps,
                workflow_definitions,
                workflow_secrets,
                users,
                tenants
            RESTART IDENTITY CASCADE;
            """);
    }
}
