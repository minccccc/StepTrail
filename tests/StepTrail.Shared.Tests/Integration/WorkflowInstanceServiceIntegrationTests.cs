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
public class WorkflowInstanceServiceIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public WorkflowInstanceServiceIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartAsync_LoadsActiveExecutableDefinition_AndMaterializesOrderedStepExecutions()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var draftDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait",
                    1,
                    new DelayStepConfiguration(5))
            ]);

        var activeDefinition = CreateWorkflowDefinition(
            version: 2,
            status: WorkflowDefinitionStatus.Active,
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers")),
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "shape-payload",
                    2,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("$.customerId", "$.id")
                    ])),
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    3,
                    new SendWebhookStepConfiguration("https://hooks.example.com/customer-created"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(draftDefinition);
            await repository.SaveNewVersionAsync(activeDefinition);
        }

        StartWorkflowResponse response;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var service = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var result = await service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = activeDefinition.Key,
                    TenantId = tenantId,
                    ExternalKey = "customer-123",
                    IdempotencyKey = "start-customer-123",
                    Input = new { customerId = "customer-123" }
                });

            response = result.Response;

            Assert.True(result.Created);
            Assert.False(response.WasAlreadyStarted);
            Assert.Equal(activeDefinition.Key, response.WorkflowKey);
            Assert.Equal(activeDefinition.Version, response.Version);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances
                .Include(workflowInstance => workflowInstance.StepExecutions)
                .AsNoTracking()
                .SingleAsync();

            Assert.Equal(response.Id, instance.Id);
            Assert.Null(instance.WorkflowDefinitionId);
            Assert.Equal(activeDefinition.Id, instance.ExecutableWorkflowDefinitionId);
            Assert.Equal(activeDefinition.Key, instance.WorkflowDefinitionKey);
            Assert.Equal(activeDefinition.Version, instance.WorkflowDefinitionVersion);

            var stepExecutions = instance.StepExecutions
                .OrderBy(stepExecution => stepExecution.StepOrder)
                .ToList();

            Assert.Equal(3, stepExecutions.Count);
            Assert.Collection(
                stepExecutions,
                stepExecution =>
                {
                    Assert.Equal(activeDefinition.StepDefinitions[0].Id, stepExecution.ExecutableStepDefinitionId);
                    Assert.Equal("fetch-customer", stepExecution.StepKey);
                    Assert.Equal(1, stepExecution.StepOrder);
                    Assert.Equal(StepType.HttpRequest.ToString(), stepExecution.StepType);
                    Assert.Equal(WorkflowStepExecutionStatus.Pending, stepExecution.Status);
                    Assert.NotNull(stepExecution.StepConfiguration);
                    Assert.Equal(response.FirstStepExecutionId, stepExecution.Id);
                },
                stepExecution =>
                {
                    Assert.Equal(activeDefinition.StepDefinitions[1].Id, stepExecution.ExecutableStepDefinitionId);
                    Assert.Equal("shape-payload", stepExecution.StepKey);
                    Assert.Equal(2, stepExecution.StepOrder);
                    Assert.Equal(StepType.Transform.ToString(), stepExecution.StepType);
                    Assert.Equal(WorkflowStepExecutionStatus.NotStarted, stepExecution.Status);
                },
                stepExecution =>
                {
                    Assert.Equal(activeDefinition.StepDefinitions[2].Id, stepExecution.ExecutableStepDefinitionId);
                    Assert.Equal("notify-partner", stepExecution.StepKey);
                    Assert.Equal(3, stepExecution.StepOrder);
                    Assert.Equal(StepType.SendWebhook.ToString(), stepExecution.StepType);
                    Assert.Equal(WorkflowStepExecutionStatus.NotStarted, stepExecution.Status);
                });
        }
    }

    [Fact]
    public async Task StartAsync_ThrowsWorkflowNotFound_WhenNoExecutableOrLegacyDefinitionExists()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new WorkflowDefinitionRepository(dbContext);
        var service = new WorkflowInstanceService(
            dbContext,
            new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
            repository);

        var ex = await Assert.ThrowsAsync<WorkflowNotFoundException>(() => service.StartAsync(
            new StartWorkflowRequest
            {
                WorkflowKey = "missing-workflow",
                TenantId = tenantId
            }));

        Assert.Equal("Workflow 'missing-workflow' (latest) is not registered.", ex.Message);
    }

    [Fact]
    public async Task StartAsync_ThrowsWorkflowDefinitionNotActive_WhenExecutableDefinitionExistsButIsNotActive()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var draftDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-before-sync",
                    1,
                    new DelayStepConfiguration(30))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(draftDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var service = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var ex = await Assert.ThrowsAsync<WorkflowDefinitionNotActiveException>(() => service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = draftDefinition.Key,
                    TenantId = tenantId
                }));

            Assert.Equal(
                $"Workflow definition '{draftDefinition.Key}' does not have an active version.",
                ex.Message);
        }
    }

    [Fact]
    public async Task StartAsync_ThrowsWorkflowNotFound_WhenExplicitExecutableVersionIsMissing()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var activeDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(activeDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var service = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var ex = await Assert.ThrowsAsync<WorkflowNotFoundException>(() => service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = activeDefinition.Key,
                    Version = 99,
                    TenantId = tenantId
                }));

            Assert.Equal(
                $"Workflow definition '{activeDefinition.Key}' v99 was not found.",
                ex.Message);
        }
    }

    [Fact]
    public async Task StartAsync_UsesCurrentActiveVersion_AndExistingInstancesRemainBoundToOriginalVersion()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var originalActiveDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers"))
            ]);

        var replacementActiveDefinition = CreateWorkflowDefinition(
            version: 2,
            status: WorkflowDefinitionStatus.Active,
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers/v2")),
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    2,
                    new SendWebhookStepConfiguration("https://hooks.example.com/customer-created"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(originalActiveDefinition);
        }

        StartWorkflowResponse firstResponse;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var service = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var firstResult = await service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = originalActiveDefinition.Key,
                    TenantId = tenantId,
                    IdempotencyKey = "customer-sync-v1"
                });

            firstResponse = firstResult.Response;
            Assert.Equal(1, firstResponse.Version);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(replacementActiveDefinition);
        }

        StartWorkflowResponse secondResponse;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var service = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var secondResult = await service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = replacementActiveDefinition.Key,
                    TenantId = tenantId,
                    IdempotencyKey = "customer-sync-v2"
                });

            secondResponse = secondResult.Response;
            Assert.Equal(2, secondResponse.Version);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedOriginalDefinition = await repository.GetByIdAsync(originalActiveDefinition.Id);
            var loadedReplacementDefinition = await repository.GetByIdAsync(replacementActiveDefinition.Id);
            var loadedActiveDefinition = await repository.GetActiveByKeyAsync(originalActiveDefinition.Key);
            var instances = await dbContext.WorkflowInstances
                .OrderBy(instance => instance.CreatedAt)
                .ToListAsync();

            Assert.NotNull(loadedOriginalDefinition);
            Assert.NotNull(loadedReplacementDefinition);
            Assert.NotNull(loadedActiveDefinition);
            Assert.Equal(WorkflowDefinitionStatus.Inactive, loadedOriginalDefinition!.Status);
            Assert.Equal(WorkflowDefinitionStatus.Active, loadedReplacementDefinition!.Status);
            Assert.Equal(replacementActiveDefinition.Id, loadedActiveDefinition!.Id);

            Assert.Equal(2, instances.Count);
            Assert.Collection(
                instances,
                firstInstance =>
                {
                    Assert.Equal(firstResponse.Id, firstInstance.Id);
                    Assert.Equal(originalActiveDefinition.Id, firstInstance.ExecutableWorkflowDefinitionId);
                    Assert.Equal(1, firstInstance.WorkflowDefinitionVersion);
                },
                secondInstance =>
                {
                    Assert.Equal(secondResponse.Id, secondInstance.Id);
                    Assert.Equal(replacementActiveDefinition.Id, secondInstance.ExecutableWorkflowDefinitionId);
                    Assert.Equal(2, secondInstance.WorkflowDefinitionVersion);
                });
        }
    }

    [Fact]
    public async Task StartAsync_AllowsSameIdempotencyKey_ForDifferentWorkflowKeys()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var customerDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers"))
            ],
            key: "customer-sync");
        var orderDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-order",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ],
            key: "order-sync");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(customerDefinition);
            await repository.SaveNewVersionAsync(orderDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var service = new WorkflowInstanceService(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var firstResult = await service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = customerDefinition.Key,
                    TenantId = tenantId,
                    IdempotencyKey = "external-delivery-123"
                });
            var secondResult = await service.StartAsync(
                new StartWorkflowRequest
                {
                    WorkflowKey = orderDefinition.Key,
                    TenantId = tenantId,
                    IdempotencyKey = "external-delivery-123"
                });

            Assert.True(firstResult.Created);
            Assert.True(secondResult.Created);
            Assert.NotEqual(firstResult.Response.Id, secondResult.Response.Id);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            Assert.Equal(2, await dbContext.WorkflowInstances.CountAsync());
            Assert.Equal(2, await dbContext.IdempotencyRecords.CountAsync());
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
        int version,
        WorkflowDefinitionStatus status,
        IReadOnlyList<StepDefinition> stepDefinitions,
        string key = "customer-sync")
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddMinutes(version);

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            key,
            "Customer Sync",
            version,
            status,
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions,
            createdAtUtc,
            updatedAtUtc,
            "Executable workflow definition used for instance creation tests.");
    }
}
