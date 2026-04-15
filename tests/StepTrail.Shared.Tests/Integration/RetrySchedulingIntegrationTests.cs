using System.Net;
using System.Text;
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
public class RetrySchedulingIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public RetrySchedulingIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FailedStep_WithExponentialRetryPolicy_SchedulesRetryWithCorrectDelay()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var retryPolicy = new RetryPolicy(
            maxAttempts: 3,
            initialDelaySeconds: 5,
            backoffStrategy: BackoffStrategy.Exponential,
            retryOnTimeout: true,
            maxDelaySeconds: 60);

        var definition = CreateWorkflowDefinition(retryPolicy);

        await using (var db = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(db);
            await repository.SaveNewVersionAsync(definition);

            var startService = new WorkflowStartService(
                db,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            await startService.StartAsync(new WorkflowStartRequest
            {
                WorkflowKey = definition.Key,
                TenantId = tenantId,
                Input = new { targetUrl = "https://api.example.com/events" }
            });
        }

        // Verify the retry policy JSON was snapshotted on the step execution
        Guid firstExecutionId;
        await using (var db = _fixture.CreateDbContext())
        {
            var execution = await db.WorkflowStepExecutions
                .Where(e => e.StepKey == "call-partner-api")
                .OrderBy(e => e.Attempt)
                .FirstAsync();

            firstExecutionId = execution.Id;
            Assert.NotNull(execution.RetryPolicyJson);
            Assert.Contains("Exponential", execution.RetryPolicyJson, StringComparison.OrdinalIgnoreCase);
        }

        // Process the step — the HTTP call will fail with 502, which is TransientFailure
        await using var provider = CreateWorkerServiceProvider(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("gateway error", Encoding.UTF8, "text/plain")
            }));

        var beforeProcess = DateTimeOffset.UtcNow;

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();
            var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();

            var claimed = await claimer.TryClaimAsync("worker-retry-test", CancellationToken.None);
            Assert.NotNull(claimed);

            await processor.ProcessAsync(claimed!, CancellationToken.None);
        }

        // Verify: attempt 1 failed, retry scheduled with exponential delay
        await using (var db = _fixture.CreateDbContext())
        {
            var failedExecution = await db.WorkflowStepExecutions.FindAsync(firstExecutionId);
            Assert.NotNull(failedExecution);
            Assert.Equal(WorkflowStepExecutionStatus.Failed, failedExecution!.Status);
            Assert.Equal("TransientFailure", failedExecution.FailureClassification);
            Assert.Equal(1, failedExecution.Attempt);

            var retryExecution = await db.WorkflowStepExecutions
                .Where(e => e.StepKey == "call-partner-api" && e.Attempt == 2)
                .FirstOrDefaultAsync();

            Assert.NotNull(retryExecution);
            Assert.Equal(WorkflowStepExecutionStatus.Pending, retryExecution!.Status);

            // Exponential: attempt 1 failed → delay = 5 * 2^(1-1) = 5s
            var expectedMinScheduledAt = beforeProcess.AddSeconds(4); // allow 1s tolerance
            var expectedMaxScheduledAt = beforeProcess.AddSeconds(10);
            Assert.True(
                retryExecution.ScheduledAt >= expectedMinScheduledAt &&
                retryExecution.ScheduledAt <= expectedMaxScheduledAt,
                $"Expected retry scheduled between {expectedMinScheduledAt:O} and {expectedMaxScheduledAt:O}, " +
                $"but was {retryExecution.ScheduledAt:O}");

            // The retry execution should carry the same policy JSON
            Assert.NotNull(retryExecution.RetryPolicyJson);

            // Verify the workflow instance is still Running (not Failed)
            var instance = await db.WorkflowInstances.FirstAsync();
            Assert.Equal(WorkflowInstanceStatus.AwaitingRetry, instance.Status);

            // Verify the StepRetryScheduled event exists
            var retryEvent = await db.WorkflowEvents
                .Where(e => e.StepExecutionId == retryExecution.Id
                         && e.EventType == WorkflowEventTypes.StepRetryScheduled)
                .FirstOrDefaultAsync();
            Assert.NotNull(retryEvent);
        }
    }

    [Fact]
    public async Task FailedStep_WithPermanentFailure_SkipsRetryDespitePolicy()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var retryPolicy = new RetryPolicy(
            maxAttempts: 5,
            initialDelaySeconds: 10,
            backoffStrategy: BackoffStrategy.Fixed);

        var definition = CreateWorkflowDefinitionWithBadConfig(retryPolicy);

        await using (var db = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(db);
            await repository.SaveNewVersionAsync(definition);

            var startService = new WorkflowStartService(
                db,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            await startService.StartAsync(new WorkflowStartRequest
            {
                WorkflowKey = definition.Key,
                TenantId = tenantId,
                Input = new { targetUrl = "https://api.example.com/events" }
            });
        }

        // Process the step — it will fail with InvalidConfiguration (missing webhook URL)
        await using var provider = CreateWorkerServiceProvider(
            _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();
            var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();

            var claimed = await claimer.TryClaimAsync("worker-retry-test", CancellationToken.None);
            Assert.NotNull(claimed);

            await processor.ProcessAsync(claimed!, CancellationToken.None);
        }

        // Verify: no retry scheduled, workflow is Failed
        await using (var db = _fixture.CreateDbContext())
        {
            var retryExecution = await db.WorkflowStepExecutions
                .Where(e => e.StepKey == "bad-webhook" && e.Attempt == 2)
                .FirstOrDefaultAsync();

            Assert.Null(retryExecution);

            var instance = await db.WorkflowInstances.FirstAsync();
            Assert.Equal(WorkflowInstanceStatus.Failed, instance.Status);
        }
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var tenantId = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Retry Scheduling Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        return tenantId;
    }

    private ServiceProvider CreateWorkerServiceProvider(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> httpHandler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Worker:DefaultLockExpirySeconds"] = "300",
                ["Worker:HeartbeatIntervalSeconds"] = "60"
            })
            .Build();

        var messageHandler = new DelegateHttpMessageHandler(httpHandler);
        var httpClient = new HttpClient(messageHandler)
        {
            BaseAddress = new Uri("https://api.example.com/")
        };

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddScoped<StepTrailDbContext>(_ => _fixture.CreateDbContext());
        services.AddScoped<AlertService>();
        services.AddScoped<StepFailureService>();
        services.AddScoped<StepExecutionClaimer>();
        services.AddScoped<StepExecutionProcessor>();
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(httpClient));
        services.AddWorkerStepExecutors();

        return services.BuildServiceProvider();
    }

    private static ExecutableWorkflowDefinition CreateWorkflowDefinition(RetryPolicy retryPolicy)
    {
        var now = DateTimeOffset.UtcNow;

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "retry-scheduling-test",
            "Retry Scheduling Test",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-partner-api",
                    1,
                    new HttpRequestStepConfiguration(
                        "https://api.example.com/events",
                        headers: new Dictionary<string, string> { ["Content-Type"] = "application/json" }),
                    retryPolicy: retryPolicy)
            ],
            now,
            now.AddMinutes(1),
            "Test workflow for retry scheduling integration tests.");
    }

    private static ExecutableWorkflowDefinition CreateWorkflowDefinitionWithBadConfig(RetryPolicy retryPolicy)
    {
        var now = DateTimeOffset.UtcNow;

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "retry-permanent-fail-test",
            "Retry Permanent Fail Test",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "bad-webhook",
                    1,
                    new SendWebhookStepConfiguration(
                        "{{input.missingField}}",
                        headers: new Dictionary<string, string>()),
                    retryPolicy: retryPolicy)
            ],
            now,
            now.AddMinutes(1),
            "Test workflow for permanent failure skip integration test.");
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _httpClient;

        public StubHttpClientFactory(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public HttpClient CreateClient(string name) => _httpClient;
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _responseFactory;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            _responseFactory(request);
    }
}
