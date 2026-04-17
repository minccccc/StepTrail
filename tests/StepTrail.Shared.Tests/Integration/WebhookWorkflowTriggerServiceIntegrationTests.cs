using System.Security.Cryptography;
using System.Text;
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
public class WebhookWorkflowTriggerServiceIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public WebhookWorkflowTriggerServiceIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StartAsync_WebhookTrigger_CapturesTriggerData_AndStartsViaSharedRuntimeFlow()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "post",
                    new WebhookSignatureValidationConfiguration(
                        "X-StepTrail-Signature",
                        "partner-signing-secret",
                        WebhookSignatureAlgorithm.HmacSha256,
                        "sha256="),
                    [
                        new WebhookInputMapping("eventId", "body.eventId"),
                        new WebhookInputMapping("customerId", "body.customer.id"),
                        new WebhookInputMapping("requestId", "headers.x-request-id"),
                        new WebhookInputMapping("delivery", "query.delivery")
                    ],
                    new WebhookIdempotencyKeyExtractionConfiguration("headers.x-idempotency-key"))),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
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
            dbContext.WorkflowSecrets.Add(new WorkflowSecret
            {
                Id = Guid.NewGuid(),
                Name = "partner-signing-secret",
                Value = "super-secret-value",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123","customer":{"id":"cus_001"}}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);
        var signature = ComputeHmacSha256Signature("super-secret-value", rawBody, "sha256=");

        StartWorkflowResponse response;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var result = await service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    ExternalKey = "evt_123",
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload,
                    Headers = new Dictionary<string, string>
                    {
                        ["x-request-id"] = "req_789",
                        ["x-steptrail-signature"] = signature,
                        ["x-idempotency-key"] = "delivery-evt-123"
                    },
                    Query = new Dictionary<string, string>
                    {
                        ["delivery"] = "retry-1"
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
            Assert.Equal("delivery-evt-123", instance.IdempotencyKey);
            Assert.NotNull(instance.TriggerData);
            Assert.NotNull(instance.Input);

            using (var triggerDataDoc = JsonDocument.Parse(instance.TriggerData!))
            {
                var root = triggerDataDoc.RootElement;
                Assert.Equal("webhook", root.GetProperty("source").GetString());
                Assert.Equal("partner-events", root.GetProperty("routeKey").GetString());
                Assert.Equal("POST", root.GetProperty("httpMethod").GetString());
                Assert.Equal(rawBody, root.GetProperty("bodyRaw").GetString());
                Assert.Equal("evt_123", root.GetProperty("body").GetProperty("eventId").GetString());
                Assert.Equal("cus_001", root.GetProperty("body").GetProperty("customer").GetProperty("id").GetString());
                Assert.Equal("delivery-evt-123", root.GetProperty("headers").GetProperty("x-idempotency-key").GetString());
                Assert.Equal("retry-1", root.GetProperty("query").GetProperty("delivery").GetString());
            }

            using (var inputDoc = JsonDocument.Parse(instance.Input!))
            {
                var root = inputDoc.RootElement;
                Assert.Equal("evt_123", root.GetProperty("eventId").GetString());
                Assert.Equal("cus_001", root.GetProperty("customerId").GetString());
                Assert.Equal("req_789", root.GetProperty("requestId").GetString());
                Assert.Equal("retry-1", root.GetProperty("delivery").GetString());
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
    public async Task StartAsync_Throws_WhenWebhookRouteDoesNotExist()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        const string rawBody = """{"eventId":"evt_123"}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        await using var dbContext = _fixture.CreateDbContext();
        var repository = new WorkflowDefinitionRepository(dbContext);
        var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
        var workflowInstanceService = WorkflowInstanceService.CreateForTest(
            dbContext,
            new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
            repository);
        var service = new WebhookWorkflowTriggerService(
            new WebhookIdempotencyKeyExtractor(),
            new WebhookInputMapper(),
            new WebhookSignatureValidationService(dbContext),
            resolver,
            workflowInstanceService);

        var ex = await Assert.ThrowsAsync<WorkflowNotFoundException>(() => service.StartAsync(
            new StartWebhookWorkflowRequest
            {
                RouteKey = "missing-route",
                TenantId = tenantId,
                HttpMethod = "POST",
                RawBody = rawBody,
                Payload = payload
            }));

        Assert.Equal("Active webhook endpoint 'missing-route' was not found.", ex.Message);
        Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task StartAsync_Throws_WhenHttpMethodDoesNotMatchConfiguredWebhookMethod()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("partner-events", "POST")),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123"}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<WebhookTriggerMethodNotAllowedException>(() => service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "GET",
                    RawBody = rawBody,
                    Payload = payload
                }));

            Assert.Equal("Webhook route 'partner-events' requires HTTP POST.", ex.Message);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenSignatureHeaderIsMissing()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "POST",
                    new WebhookSignatureValidationConfiguration(
                        "X-StepTrail-Signature",
                        "partner-signing-secret",
                        WebhookSignatureAlgorithm.HmacSha256,
                        "sha256="))),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.WorkflowSecrets.Add(new WorkflowSecret
            {
                Id = Guid.NewGuid(),
                Name = "partner-signing-secret",
                Value = "super-secret-value",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123"}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<WebhookTriggerSignatureValidationException>(() => service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload,
                    Headers = new Dictionary<string, string>()
                }));

            Assert.Equal("Missing webhook signature header 'X-StepTrail-Signature'.", ex.Message);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenSignatureIsInvalid()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "POST",
                    new WebhookSignatureValidationConfiguration(
                        "X-StepTrail-Signature",
                        "partner-signing-secret",
                        WebhookSignatureAlgorithm.HmacSha256,
                        "sha256="))),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            dbContext.WorkflowSecrets.Add(new WorkflowSecret
            {
                Id = Guid.NewGuid(),
                Name = "partner-signing-secret",
                Value = "super-secret-value",
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync();

            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123"}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<WebhookTriggerSignatureValidationException>(() => service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload,
                    Headers = new Dictionary<string, string>
                    {
                        ["x-steptrail-signature"] = "sha256=not-a-real-signature"
                    }
                }));

            Assert.Equal("Invalid webhook signature in header 'X-StepTrail-Signature'.", ex.Message);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_Throws_WhenWebhookInputMappingSourceFieldIsMissing()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "POST",
                    inputMappings:
                    [
                        new WebhookInputMapping("customerId", "body.customer.id"),
                        new WebhookInputMapping("requestId", "headers.x-request-id")
                    ])),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123"}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var ex = await Assert.ThrowsAsync<WebhookTriggerInputMappingException>(() => service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload,
                    Headers = new Dictionary<string, string>()
                }));

            Assert.Contains("body.customer.id", ex.Message, StringComparison.Ordinal);
            Assert.Equal(0, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_WhenWebhookIdempotencyHeaderIsConfigured_ReturnsExistingInstance_ForDuplicateDelivery()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "POST",
                    idempotencyKeyExtraction: new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id"))),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123"}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        StartWorkflowResponse firstResponse;
        StartWorkflowResponse secondResponse;
        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var firstResult = await service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload,
                    Headers = new Dictionary<string, string>
                    {
                        ["x-delivery-id"] = "delivery-evt-123"
                    }
                });

            var secondResult = await service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload,
                    Headers = new Dictionary<string, string>
                    {
                        ["x-delivery-id"] = "delivery-evt-123"
                    }
                });

            firstResponse = firstResult.Response;
            secondResponse = secondResult.Response;

            Assert.True(firstResult.Created);
            Assert.False(secondResult.Created);
            Assert.True(secondResponse.WasAlreadyStarted);
            Assert.Equal(firstResponse.Id, secondResponse.Id);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            Assert.Equal(1, await dbContext.WorkflowInstances.CountAsync());
        }
    }

    [Fact]
    public async Task StartAsync_WhenWebhookIdempotencyComesFromBody_PersistsExtractedKey()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "POST",
                    idempotencyKeyExtraction: new WebhookIdempotencyKeyExtractionConfiguration("body.event.id"))),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"event":{"id":"evt_123"}}""";
        var payload = JsonSerializer.Deserialize<JsonElement>(rawBody);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            var result = await service.StartAsync(
                new StartWebhookWorkflowRequest
                {
                    RouteKey = "partner-events",
                    TenantId = tenantId,
                    HttpMethod = "POST",
                    RawBody = rawBody,
                    Payload = payload
                });

            Assert.True(result.Created);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var instance = await dbContext.WorkflowInstances.SingleAsync();
            Assert.Equal("evt_123", instance.IdempotencyKey);
        }
    }

    [Fact]
    public async Task StartAsync_WhenDuplicateDeliveriesRace_OnlyCreatesOneInstance()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateWorkflowDefinition(
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(
                    "partner-events",
                    "POST",
                    idempotencyKeyExtraction: new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id"))),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/orders"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(definition);
        }

        const string rawBody = """{"eventId":"evt_123"}""";
        var requestFactory = () => new StartWebhookWorkflowRequest
        {
            RouteKey = "partner-events",
            TenantId = tenantId,
            HttpMethod = "POST",
            RawBody = rawBody,
            Payload = JsonSerializer.Deserialize<JsonElement>(rawBody),
            Headers = new Dictionary<string, string>
            {
                ["x-delivery-id"] = "delivery-evt-123"
            }
        };

        Task<(StartWorkflowResponse Response, bool Created)> StartInIsolatedScopeAsync()
        {
            var dbContext = _fixture.CreateDbContext();
            var repository = new WorkflowDefinitionRepository(dbContext);
            var resolver = new ExecutableWorkflowTriggerResolver(dbContext, repository);
            var workflowInstanceService = WorkflowInstanceService.CreateForTest(
                dbContext,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);
            var service = new WebhookWorkflowTriggerService(
                new WebhookIdempotencyKeyExtractor(),
                new WebhookInputMapper(),
                new WebhookSignatureValidationService(dbContext),
                resolver,
                workflowInstanceService);

            return StartAndDisposeAsync(service, dbContext, requestFactory());
        }

        var results = await Task.WhenAll(StartInIsolatedScopeAsync(), StartInIsolatedScopeAsync());

        Assert.Single(results.Select(result => result.Response.Id).Distinct());
        Assert.Equal(1, results.Count(result => result.Created));
        Assert.Equal(1, results.Count(result => !result.Created));

        await using (var dbContext = _fixture.CreateDbContext())
        {
            Assert.Equal(1, await dbContext.WorkflowInstances.CountAsync());
            Assert.Equal(1, await dbContext.IdempotencyRecords.CountAsync());
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
            "Executable workflow definition used for webhook trigger tests.");
    }

    private static string ComputeHmacSha256Signature(string secretValue, string rawBody, string? prefix = null)
    {
        var hashBytes = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secretValue),
            Encoding.UTF8.GetBytes(rawBody));
        var signature = Convert.ToHexString(hashBytes).ToLowerInvariant();

        return string.IsNullOrEmpty(prefix)
            ? signature
            : prefix + signature;
    }

    private static async Task<(StartWorkflowResponse Response, bool Created)> StartAndDisposeAsync(
        WebhookWorkflowTriggerService service,
        StepTrailDbContext dbContext,
        StartWebhookWorkflowRequest request)
    {
        await using (dbContext)
        {
            return await service.StartAsync(request);
        }
    }
}
