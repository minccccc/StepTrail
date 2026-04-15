using System.Net;
using System.Text;
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
public class AttemptHistoryIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public AttemptHistoryIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MultiAttemptStep_PersistsFullAttemptTimeline()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var retryPolicy = new RetryPolicy(
            maxAttempts: 3,
            initialDelaySeconds: 5,
            backoffStrategy: BackoffStrategy.Fixed);

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
                Input = new { value = "test" }
            });
        }

        // --- Attempt 1: fails with 502 ---
        var callCount = 0;

        await using var provider = CreateWorkerServiceProvider(request =>
        {
            callCount++;
            if (callCount == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
                {
                    Content = new StringContent("gateway error", Encoding.UTF8, "text/plain")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"status":"ok"}""", Encoding.UTF8, "application/json")
            });
        });

        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();
            var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();

            var claimed = await claimer.TryClaimAsync("worker-attempt-1", CancellationToken.None);
            Assert.NotNull(claimed);
            Assert.Equal(1, claimed!.Attempt);

            await processor.ProcessAsync(claimed, CancellationToken.None);
        }

        // --- Verify attempt 1 failed and attempt 2 is pending ---
        Guid retryExecutionId;
        await using (var db = _fixture.CreateDbContext())
        {
            var attempt1 = await db.WorkflowStepExecutions
                .Where(e => e.StepKey == "call-api" && e.Attempt == 1)
                .FirstAsync();

            Assert.Equal(WorkflowStepExecutionStatus.Failed, attempt1.Status);
            Assert.Equal("TransientFailure", attempt1.FailureClassification);
            Assert.NotNull(attempt1.StartedAt);
            Assert.NotNull(attempt1.CompletedAt);
            Assert.NotNull(attempt1.Error);

            // Verify failure event has payload with retry metadata
            var failureEvent = await db.WorkflowEvents
                .Where(e => e.StepExecutionId == attempt1.Id && e.EventType == WorkflowEventTypes.StepFailed)
                .FirstAsync();

            Assert.NotNull(failureEvent.Payload);
            using var failurePayload = JsonDocument.Parse(failureEvent.Payload!);
            Assert.Equal("TransientFailure", failurePayload.RootElement.GetProperty("classification").GetString());
            Assert.Equal(1, failurePayload.RootElement.GetProperty("attempt").GetInt32());
            Assert.Equal(3, failurePayload.RootElement.GetProperty("maxAttempts").GetInt32());
            Assert.True(failurePayload.RootElement.GetProperty("retryScheduled").GetBoolean());

            var attempt2 = await db.WorkflowStepExecutions
                .Where(e => e.StepKey == "call-api" && e.Attempt == 2)
                .FirstAsync();

            retryExecutionId = attempt2.Id;
            Assert.Equal(WorkflowStepExecutionStatus.Pending, attempt2.Status);

            // Verify retry scheduled event has payload with delay details
            var retryEvent = await db.WorkflowEvents
                .Where(e => e.StepExecutionId == attempt2.Id
                         && e.EventType == WorkflowEventTypes.StepRetryScheduled)
                .FirstAsync();

            Assert.NotNull(retryEvent.Payload);
            using var retryPayload = JsonDocument.Parse(retryEvent.Payload!);
            Assert.Equal(2, retryPayload.RootElement.GetProperty("nextAttempt").GetInt32());
            Assert.Equal(5, retryPayload.RootElement.GetProperty("delaySeconds").GetInt32());
            Assert.Equal("Fixed", retryPayload.RootElement.GetProperty("backoffStrategy").GetString());
        }

        // --- Make retry immediately claimable ---
        await using (var db = _fixture.CreateDbContext())
        {
            var retryExecution = await db.WorkflowStepExecutions.FindAsync(retryExecutionId);
            retryExecution!.ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(-1);
            retryExecution.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // --- Attempt 2: succeeds ---
        await using (var scope = provider.CreateAsyncScope())
        {
            var claimer = scope.ServiceProvider.GetRequiredService<StepExecutionClaimer>();
            var processor = scope.ServiceProvider.GetRequiredService<StepExecutionProcessor>();

            var claimed = await claimer.TryClaimAsync("worker-attempt-2", CancellationToken.None);
            Assert.NotNull(claimed);
            Assert.Equal(retryExecutionId, claimed!.Id);
            Assert.Equal(2, claimed.Attempt);

            await processor.ProcessAsync(claimed, CancellationToken.None);
        }

        // --- Verify complete attempt timeline ---
        await using (var db = _fixture.CreateDbContext())
        {
            var attempts = await db.WorkflowStepExecutions
                .Where(e => e.StepKey == "call-api")
                .OrderBy(e => e.Attempt)
                .ToListAsync();

            Assert.Equal(2, attempts.Count);

            // Attempt 1: Failed
            var a1 = attempts[0];
            Assert.Equal(1, a1.Attempt);
            Assert.Equal(WorkflowStepExecutionStatus.Failed, a1.Status);
            Assert.Equal("TransientFailure", a1.FailureClassification);
            Assert.NotNull(a1.StartedAt);
            Assert.NotNull(a1.CompletedAt);
            Assert.True(a1.CompletedAt > a1.StartedAt);

            // Attempt 2: Completed
            var a2 = attempts[1];
            Assert.Equal(2, a2.Attempt);
            Assert.Equal(WorkflowStepExecutionStatus.Completed, a2.Status);
            Assert.Null(a2.FailureClassification);
            Assert.NotNull(a2.StartedAt);
            Assert.NotNull(a2.CompletedAt);
            Assert.True(a2.CompletedAt > a2.StartedAt);
            Assert.NotNull(a2.Output);

            // Workflow should be completed (only one step)
            var instance = await db.WorkflowInstances.FirstAsync();
            Assert.Equal(WorkflowInstanceStatus.Completed, instance.Status);

            // Verify full event timeline
            var events = await db.WorkflowEvents
                .Where(e => e.WorkflowInstanceId == instance.Id)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync();

            var eventTypes = events.Select(e => e.EventType).ToList();

            Assert.Contains(WorkflowEventTypes.WorkflowStarted, eventTypes);
            Assert.Contains(WorkflowEventTypes.StepStarted, eventTypes);
            Assert.Contains(WorkflowEventTypes.StepFailed, eventTypes);
            Assert.Contains(WorkflowEventTypes.StepRetryScheduled, eventTypes);
            Assert.Contains(WorkflowEventTypes.StepCompleted, eventTypes);
            Assert.Contains(WorkflowEventTypes.WorkflowCompleted, eventTypes);
        }
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var tenantId = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Attempt History Test Tenant",
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
            "attempt-history-test",
            "Attempt History Test",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-api",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/events"),
                    retryPolicy: retryPolicy)
            ],
            now,
            now.AddMinutes(1),
            "Test workflow for attempt history integration tests.");
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
