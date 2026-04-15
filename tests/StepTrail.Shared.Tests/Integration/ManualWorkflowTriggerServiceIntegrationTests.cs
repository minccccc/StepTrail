using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Models;
using StepTrail.Api.Services;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Tests.Infrastructure;
using StepTrail.Shared.Workflows;
using Xunit;
using ExecutableWorkflowDefinition = StepTrail.Shared.Definitions.WorkflowDefinition;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class ManualWorkflowTriggerServiceIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public ManualWorkflowTriggerServiceIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartAsync_ManualTrigger_CapturesTriggerData_AndStartsViaSharedRuntimeFlow()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-partner",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders")),
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait",
                    2,
                    new DelayStepConfiguration(15))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        StartWorkflowResponse response;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new ManualWorkflowTriggerService(
                resolver,
                workflowInstanceService);

            var result = await service.StartAsync(
                new StartManualWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    ExternalKey = "order-42",
                    IdempotencyKey = "manual-order-42",
                    ActorId = "ops-admin",
                    Payload = new
                    {
                        orderId = "order-42",
                        amount = 49.95m
                    }
                });

            response = result.Response;

            Assert.True(result.Created);
            Assert.Equal(definition.Key, response.WorkflowKey);
            Assert.Equal(definition.Version, response.Version);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances
                .Include(workflowInstance => workflowInstance.StepExecutions)
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(definition.Id, instance.ExecutableWorkflowDefinitionId);
            Assert.NotNull(instance.TriggerData);
            Assert.NotNull(instance.Input);

            using (var triggerDataDoc = JsonDocument.Parse(instance.TriggerData!))
            {
                var root = triggerDataDoc.RootElement;
                Assert.Equal("manual", root.GetProperty("source").GetString());
                Assert.Equal("ops-console", root.GetProperty("entryPointKey").GetString());
                Assert.Equal("ops-admin", root.GetProperty("actorId").GetString());
                Assert.Equal("order-42", root.GetProperty("payload").GetProperty("orderId").GetString());
            }

            using (var inputDoc = JsonDocument.Parse(instance.Input!))
            {
                var root = inputDoc.RootElement;
                Assert.Equal("order-42", root.GetProperty("orderId").GetString());
                Assert.Equal(49.95m, root.GetProperty("amount").GetDecimal());
            }

            var stepExecutions = instance.StepExecutions
                .OrderBy(stepExecution => stepExecution.StepOrder)
                .ToList();

            Assert.Equal(2, stepExecutions.Count);
            Assert.Equal(WorkflowStepExecutionStatus.Pending, stepExecutions[0].Status);
            Assert.Equal(WorkflowStepExecutionStatus.NotStarted, stepExecutions[1].Status);
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenWorkflowDoesNotUseManualTrigger()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("test-route")),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-partner",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new ManualWorkflowTriggerService(
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<WorkflowTriggerMismatchException>(() => service.StartAsync(
                new StartManualWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    Payload = new { orderId = "order-42" }
                }));

            Assert.Equal(
                $"Workflow definition '{definition.Key}' v{definition.Version} does not support manual trigger starts.",
                ex.Message);

            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_AllowsNullPayload_ForZeroInputWorkflow()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait",
                    1,
                    new DelayStepConfiguration(10))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new ManualWorkflowTriggerService(
                resolver,
                workflowInstanceService);

            var result = await service.StartAsync(
                new StartManualWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    Payload = null
                });

            Assert.True(result.Created);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances.SingleAsync();

            Assert.Null(instance.Input);
            Assert.NotNull(instance.TriggerData);

            using var triggerDataDoc = JsonDocument.Parse(instance.TriggerData!);
            Assert.Equal(JsonValueKind.Null, triggerDataDoc.RootElement.GetProperty("payload").ValueKind);
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

    private static ExecutableWorkflowDefinition CreateWorkflowDefinition(
        TriggerDefinition triggerDefinition,
        IReadOnlyList<StepDefinition> stepDefinitions)
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "customer-sync",
            "Customer Sync",
            1,
            WorkflowDefinitionStatus.Active,
            triggerDefinition,
            stepDefinitions,
            createdAtUtc,
            createdAtUtc.AddMinutes(1),
            "Executable workflow definition used for manual trigger tests.");
    }
}
