using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Tests.Infrastructure;
using StepTrail.Shared.Workflows;
using StepTrail.Worker;
using Xunit;
using ExecutableWorkflowDefinition = StepTrail.Shared.Definitions.WorkflowDefinition;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class RecurringWorkflowDispatcherIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public RecurringWorkflowDispatcherIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DispatchDueSchedulesAsync_StartsExecutableScheduledWorkflow_ThroughSharedStartFlow()
    {
        await _fixture.ResetAsync();
        await CreateDefaultTenantAsync();

        var definition = CreateScheduledWorkflowDefinition(intervalSeconds: 300);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var dispatcher = new RecurringWorkflowDispatcher(
                dbContext,
                repository,
                new WorkflowStartService(dbContext, new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()), repository),
                NullLogger<RecurringWorkflowDispatcher>.Instance);

            var firedCount = await dispatcher.DispatchDueSchedulesAsync(CancellationToken.None);

            Assert.Equal(1, firedCount);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances
                .Include(workflowInstance => workflowInstance.StepExecutions)
                .SingleAsync();
            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();

            Assert.Equal(definition.Id, instance.ExecutableWorkflowDefinitionId);
            Assert.Equal(definition.Key, instance.WorkflowDefinitionKey);
            Assert.Equal(definition.Version, instance.WorkflowDefinitionVersion);
            Assert.NotNull(instance.TriggerData);
            Assert.NotNull(instance.Input);

            using (var triggerDataDocument = JsonDocument.Parse(instance.TriggerData!))
            {
                var root = triggerDataDocument.RootElement;
                Assert.Equal("schedule", root.GetProperty("source").GetString());
                Assert.Equal(schedule.Id, root.GetProperty("scheduleId").GetGuid());
                Assert.Equal(definition.Key, root.GetProperty("workflowKey").GetString());
                Assert.Equal(300, root.GetProperty("intervalSeconds").GetInt32());
            }

            using (var inputDocument = JsonDocument.Parse(instance.Input!))
            {
                var root = inputDocument.RootElement;
                Assert.Equal(300, root.GetProperty("intervalSeconds").GetInt32());
                Assert.True(root.TryGetProperty("scheduledAtUtc", out _));
            }

            Assert.Single(instance.StepExecutions);
            Assert.Equal(WorkflowStepExecutionStatus.Pending, instance.StepExecutions.Single().Status);
            Assert.NotNull(schedule.LastRunAt);
            Assert.True(schedule.NextRunAt > schedule.LastRunAt);
        }
    }

    [Fact]
    public async Task DispatchDueSchedulesAsync_DoesNotStartWorkflow_WhenExecutableScheduleIsNotDue()
    {
        await _fixture.ResetAsync();
        await CreateDefaultTenantAsync();

        var definition = CreateScheduledWorkflowDefinition(intervalSeconds: 300);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);

            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();
            schedule.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(10);
            schedule.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var dispatcher = new RecurringWorkflowDispatcher(
                dbContext,
                repository,
                new WorkflowStartService(dbContext, new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()), repository),
                NullLogger<RecurringWorkflowDispatcher>.Instance);

            var firedCount = await dispatcher.DispatchDueSchedulesAsync(CancellationToken.None);

            Assert.Equal(0, firedCount);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task DispatchDueSchedulesAsync_StartsExecutableCronScheduledWorkflow_AndCalculatesNextCronRun()
    {
        await _fixture.ResetAsync();
        await CreateDefaultTenantAsync();

        var definition = CreateCronScheduledWorkflowDefinition("0 * * * *");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);

            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();
            schedule.NextRunAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            schedule.UpdatedAt = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync();
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var dispatcher = new RecurringWorkflowDispatcher(
                dbContext,
                repository,
                new WorkflowStartService(dbContext, new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()), repository),
                NullLogger<RecurringWorkflowDispatcher>.Instance);

            var firedCount = await dispatcher.DispatchDueSchedulesAsync(CancellationToken.None);

            Assert.Equal(1, firedCount);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances.SingleAsync();
            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();

            using (var triggerDataDocument = JsonDocument.Parse(instance.TriggerData!))
            {
                var root = triggerDataDocument.RootElement;
                Assert.Equal("0 * * * *", root.GetProperty("cronExpression").GetString());
            }

            using (var inputDocument = JsonDocument.Parse(instance.Input!))
            {
                var root = inputDocument.RootElement;
                Assert.Equal("0 * * * *", root.GetProperty("cronExpression").GetString());
            }

            Assert.Null(schedule.IntervalSeconds);
            Assert.Equal("0 * * * *", schedule.CronExpression);
            Assert.NotNull(schedule.LastRunAt);
            Assert.Equal(0, schedule.NextRunAt.Minute);
            Assert.Equal(0, schedule.NextRunAt.Second);
            Assert.True(schedule.NextRunAt > schedule.LastRunAt);
        }
    }

    private async Task CreateDefaultTenantAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Tenants.Add(new Tenant
        {
            Id = StepTrailRuntimeDefaults.DefaultTenantId,
            Name = "Default",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }

    private static ExecutableWorkflowDefinition CreateScheduledWorkflowDefinition(int intervalSeconds)
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "scheduled-customer-sync",
            "Scheduled Customer Sync",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateSchedule(
                Guid.NewGuid(),
                new ScheduleTriggerConfiguration(intervalSeconds)),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-upstream",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/sync"))
            ],
            createdAtUtc,
            createdAtUtc.AddMinutes(1),
            "Executable workflow definition used for recurring schedule dispatch tests.");
    }

    private static ExecutableWorkflowDefinition CreateCronScheduledWorkflowDefinition(string cronExpression)
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "scheduled-customer-sync-cron",
            "Scheduled Customer Sync Cron",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateSchedule(
                Guid.NewGuid(),
                new ScheduleTriggerConfiguration(cronExpression)),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-upstream",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/sync"))
            ],
            createdAtUtc,
            createdAtUtc.AddMinutes(1),
            "Executable workflow definition used for recurring cron schedule dispatch tests.");
    }
}
