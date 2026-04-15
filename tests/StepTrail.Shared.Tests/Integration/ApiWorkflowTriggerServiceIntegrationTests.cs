using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
public class ApiWorkflowTriggerServiceIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public ApiWorkflowTriggerServiceIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartAsync_ApiTrigger_CapturesTriggerData_AndStartsViaSharedRuntimeFlow()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
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
            var authService = new ApiTriggerAuthenticationService(
                Options.Create(new ApiTriggerAuthenticationOptions
                {
                    SharedSecret = "test-api-key"
                }));
            var service = new ApiWorkflowTriggerService(
                authService,
                resolver,
                workflowInstanceService);

            var result = await service.StartAsync(
                new StartApiWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    ExternalKey = "customer-123",
                    IdempotencyKey = "api-customer-123",
                    ApiKey = "test-api-key",
                    Payload = new
                    {
                        customerId = "customer-123",
                        priority = "high"
                    },
                    Headers = new Dictionary<string, string>
                    {
                        ["x-idempotency-key"] = "api-customer-123",
                        ["x-external-key"] = "customer-123"
                    },
                    Query = new Dictionary<string, string>
                    {
                        ["debug"] = "true"
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
                Assert.Equal("api", root.GetProperty("source").GetString());
                Assert.Equal("start-customer-sync", root.GetProperty("operationKey").GetString());
                Assert.Equal("customer-123", root.GetProperty("payload").GetProperty("customerId").GetString());
                Assert.Equal("api-customer-123", root.GetProperty("headers").GetProperty("x-idempotency-key").GetString());
                Assert.Equal("true", root.GetProperty("query").GetProperty("debug").GetString());
            }

            using (var inputDoc = JsonDocument.Parse(instance.Input!))
            {
                var root = inputDoc.RootElement;
                Assert.Equal("customer-123", root.GetProperty("customerId").GetString());
                Assert.Equal("high", root.GetProperty("priority").GetString());
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
    public async Task StartAsync_Throws_WhenWorkflowDoesNotUseApiTrigger()
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
            var authService = new ApiTriggerAuthenticationService(
                Options.Create(new ApiTriggerAuthenticationOptions
                {
                    SharedSecret = "test-api-key"
                }));
            var service = new ApiWorkflowTriggerService(
                authService,
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<WorkflowTriggerMismatchException>(() => service.StartAsync(
                new StartApiWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    ApiKey = "test-api-key",
                    Payload = new { customerId = "customer-123" }
                }));

            Assert.Equal(
                $"Workflow definition '{definition.Key}' v{definition.Version} does not support API trigger starts.",
                ex.Message);

            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenApiKeyIsMissing()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
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
            var authService = new ApiTriggerAuthenticationService(
                Options.Create(new ApiTriggerAuthenticationOptions
                {
                    SharedSecret = "test-api-key"
                }));
            var service = new ApiWorkflowTriggerService(
                authService,
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<ApiTriggerAuthenticationException>(() => service.StartAsync(
                new StartApiWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    Payload = new { customerId = "customer-123" }
                }));

            Assert.Contains("Missing API trigger credential", ex.Message, StringComparison.Ordinal);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenApiKeyIsInvalid()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
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
            var authService = new ApiTriggerAuthenticationService(
                Options.Create(new ApiTriggerAuthenticationOptions
                {
                    SharedSecret = "test-api-key"
                }));
            var service = new ApiWorkflowTriggerService(
                authService,
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<ApiTriggerAuthenticationException>(() => service.StartAsync(
                new StartApiWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    ApiKey = "wrong-api-key",
                    Payload = new { customerId = "customer-123" }
                }));

            Assert.Equal("Invalid API trigger credential.", ex.Message);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenAuthenticationIsUnconfigured_AndUnauthenticatedModeIsNotExplicitlyEnabled()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
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
            var authService = new ApiTriggerAuthenticationService(
                Options.Create(new ApiTriggerAuthenticationOptions()));
            var service = new ApiWorkflowTriggerService(
                authService,
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<ApiTriggerAuthenticationConfigurationException>(() => service.StartAsync(
                new StartApiWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    Payload = new { customerId = "customer-123" }
                }));

            Assert.Contains("API trigger authentication is not configured", ex.Message, StringComparison.Ordinal);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_AllowsRequest_WhenAuthenticationIsExplicitlyDisabled()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
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
            var authService = new ApiTriggerAuthenticationService(
                Options.Create(new ApiTriggerAuthenticationOptions
                {
                    AllowUnauthenticated = true
                }));
            var service = new ApiWorkflowTriggerService(
                authService,
                resolver,
                workflowInstanceService);

            var result = await service.StartAsync(
                new StartApiWorkflowRequest
                {
                    WorkflowKey = definition.Key,
                    TenantId = tenantId,
                    Payload = new { customerId = "customer-123" }
                });

            Assert.True(result.Created);
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
            "Executable workflow definition used for API trigger tests.");
    }
}
