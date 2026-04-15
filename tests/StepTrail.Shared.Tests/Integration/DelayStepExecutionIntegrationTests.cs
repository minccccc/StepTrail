using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Tests.Infrastructure;
using StepTrail.Shared.Workflows;
using StepTrail.Worker;
using StepTrail.Worker.Alerts;
using StepTrail.Worker.StepExecutors;
using Xunit;
using ExecutableWorkflowDefinition = StepTrail.Shared.Definitions.WorkflowDefinition;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class DelayStepExecutionIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public DelayStepExecutionIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DelayStep_PausesAndResumes_WhenScheduledTimeIsReached()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition();

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);

            var startService = new WorkflowStartService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            await startService.StartAsync(
                new WorkflowStartRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    Input = new { customerId = "cus_123" }
                });
        }

        await using var provider = CreateWorkerServiceProvider();

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();
            var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();

            var claimed = await claimer.TryClaimAsync("worker-delay-test", CancellationToken.None);
            Assert.NotNull(claimed);

            await processor.ProcessAsync(claimed!, CancellationToken.None);
        }

        Guid delayExecutionId;

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances
                .Include(workflowInstance => workflowInstance.StepExecutions)
                .SingleAsync();

            var delayExecution = instance.StepExecutions.Single(stepExecution => stepExecution.StepKey == "wait-before-follow-up");
            var nextExecution = instance.StepExecutions.Single(stepExecution => stepExecution.StepKey == "shape-follow-up");

            delayExecutionId = delayExecution.Id;

            Assert.Equal(WorkflowInstanceStatus.Running, instance.Status);
            Assert.Equal(WorkflowStepExecutionStatus.Waiting, delayExecution.Status);
            Assert.NotNull(delayExecution.Output);
            Assert.Null(delayExecution.CompletedAt);
            Assert.True(delayExecution.ScheduledAt > DateTimeOffset.UtcNow.AddSeconds(20));
            Assert.Equal(WorkflowStepExecutionStatus.NotStarted, nextExecution.Status);

            var waitingEvent = await dbContext.WorkflowEvents
                .SingleAsync(workflowEvent => workflowEvent.StepExecutionId == delayExecution.Id
                                           && workflowEvent.EventType == WorkflowEventTypes.StepWaiting);
            Assert.NotNull(waitingEvent.Payload);

            using var outputDocument = JsonDocument.Parse(delayExecution.Output!);
            Assert.Equal("fixed", outputDocument.RootElement.GetProperty("delayType").GetString());
            Assert.Equal("00:00:30", outputDocument.RootElement.GetProperty("requestedDuration").GetString());
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var delayExecution = await dbContext.WorkflowStepExecutions
                .SingleAsync(stepExecution => stepExecution.Id == delayExecutionId);

            delayExecution.ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            delayExecution.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();
            var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();

            var claimed = await claimer.TryClaimAsync("worker-delay-test", CancellationToken.None);
            Assert.NotNull(claimed);
            Assert.Equal(delayExecutionId, claimed!.Id);

            await processor.ProcessAsync(claimed, CancellationToken.None);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances
                .Include(workflowInstance => workflowInstance.StepExecutions)
                .SingleAsync();

            var delayExecution = instance.StepExecutions.Single(stepExecution => stepExecution.StepKey == "wait-before-follow-up");
            var nextExecution = instance.StepExecutions.Single(stepExecution => stepExecution.StepKey == "shape-follow-up");

            Assert.Equal(WorkflowInstanceStatus.Running, instance.Status);
            Assert.Equal(WorkflowStepExecutionStatus.Completed, delayExecution.Status);
            Assert.NotNull(delayExecution.CompletedAt);
            Assert.Equal(WorkflowStepExecutionStatus.Pending, nextExecution.Status);
            Assert.Equal(delayExecution.Output, nextExecution.Input);
        }
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var tenantId = Guid.NewGuid();

        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Integration Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        return tenantId;
    }

    private ServiceProvider CreateWorkerServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:DefaultLockExpirySeconds"] = "300",
                ["Worker:HeartbeatIntervalSeconds"] = "60"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<StepTrailDbContext>(_ => _fixture.CreateDbContext());
        services.AddScoped<AlertService>();
        services.AddScoped<StepFailureService>();
        services.AddScoped<StepExecutionClaimer>();
        services.AddScoped<StepExecutionProcessor>();
        services.AddWorkerStepExecutors();

        return services.BuildServiceProvider();
    }

    private static ExecutableWorkflowDefinition CreateWorkflowDefinition()
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 15, 12, 0, 0, TimeSpan.Zero);

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "delay-follow-up",
            "Delay Follow-up",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-before-follow-up",
                    1,
                    new DelayStepConfiguration(30)),
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "shape-follow-up",
                    2,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("customerId", "{{input.customerId}}")
                    ]))
            ],
            createdAtUtc,
            createdAtUtc.AddMinutes(1),
            "Executable workflow definition used for delay step execution tests.");
    }
}
