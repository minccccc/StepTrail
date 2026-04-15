using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Services;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Tests.Infrastructure;
using StepTrail.Shared.Workflows;
using Xunit;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class ManualRetryIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public ManualRetryIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RetryAsync_FailedWorkflow_CreatesNewExecutionAndMovesToRunning()
    {
        await _fixture.ResetAsync();
        var (instanceId, failedExecutionId) = await SeedFailedWorkflowAsync();

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var response = await service.RetryAsync(instanceId, CancellationToken.None);

        Assert.Equal(instanceId, response.InstanceId);
        Assert.Equal("Running", response.InstanceStatus);
        Assert.Equal("call-api", response.StepKey);
        Assert.NotEqual(failedExecutionId, response.NewStepExecutionId);

        // Verify the new execution
        var newExecution = await db.WorkflowStepExecutions.FindAsync(response.NewStepExecutionId);
        Assert.NotNull(newExecution);
        Assert.Equal(WorkflowStepExecutionStatus.Pending, newExecution!.Status);
        Assert.Equal(1, newExecution.Attempt);
        Assert.Equal("call-api", newExecution.StepKey);

        // Verify workflow is Running
        var instance = await db.WorkflowInstances.FindAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance!.Status);
        Assert.Null(instance.CompletedAt);

        // Verify WorkflowRetried event has payload with origin
        var retriedEvent = await db.WorkflowEvents
            .Where(e => e.WorkflowInstanceId == instanceId
                     && e.EventType == WorkflowEventTypes.WorkflowRetried)
            .FirstOrDefaultAsync();

        Assert.NotNull(retriedEvent);
        Assert.NotNull(retriedEvent!.Payload);

        using var payload = JsonDocument.Parse(retriedEvent.Payload!);
        Assert.Equal("manual", payload.RootElement.GetProperty("origin").GetString());
        Assert.Equal("call-api", payload.RootElement.GetProperty("stepKey").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("newAttempt").GetInt32());
    }

    [Fact]
    public async Task RetryAsync_RunningWorkflow_Throws()
    {
        await _fixture.ResetAsync();
        var instanceId = await SeedWorkflowWithStatusAsync(WorkflowInstanceStatus.Running);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.RetryAsync(instanceId, CancellationToken.None));

        Assert.Contains("Running", ex.Message);
    }

    [Fact]
    public async Task RetryAsync_CompletedWorkflow_Throws()
    {
        await _fixture.ResetAsync();
        var instanceId = await SeedWorkflowWithStatusAsync(WorkflowInstanceStatus.Completed);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.RetryAsync(instanceId, CancellationToken.None));

        Assert.Contains("Completed", ex.Message);
    }

    [Fact]
    public async Task RetryAsync_AwaitingRetryWorkflow_Throws()
    {
        await _fixture.ResetAsync();
        var instanceId = await SeedWorkflowWithStatusAsync(WorkflowInstanceStatus.AwaitingRetry);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.RetryAsync(instanceId, CancellationToken.None));

        Assert.Contains("AwaitingRetry", ex.Message);
    }

    [Fact]
    public async Task RetryAsync_NonExistentWorkflow_Throws()
    {
        await _fixture.ResetAsync();

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        await Assert.ThrowsAsync<WorkflowInstanceNotFoundException>(() =>
            service.RetryAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task RetryAsync_PreservesRetryPolicyJson()
    {
        await _fixture.ResetAsync();
        var retryPolicyJson = """{"maxAttempts":5,"initialDelaySeconds":15,"backoffStrategy":"Exponential","retryOnTimeout":true,"maxDelaySeconds":120}""";

        var (instanceId, _) = await SeedFailedWorkflowAsync(retryPolicyJson: retryPolicyJson);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var response = await service.RetryAsync(instanceId, CancellationToken.None);

        var newExecution = await db.WorkflowStepExecutions.FindAsync(response.NewStepExecutionId);
        Assert.NotNull(newExecution!.RetryPolicyJson);
        // Round-trip through DB may normalize formatting; verify the content
        using var expected = JsonDocument.Parse(retryPolicyJson);
        using var actual = JsonDocument.Parse(newExecution.RetryPolicyJson!);
        Assert.Equal(5, actual.RootElement.GetProperty("maxAttempts").GetInt32());
        Assert.Equal(15, actual.RootElement.GetProperty("initialDelaySeconds").GetInt32());
        Assert.Equal("Exponential", actual.RootElement.GetProperty("backoffStrategy").GetString());
        Assert.Equal(120, actual.RootElement.GetProperty("maxDelaySeconds").GetInt32());
    }

    private async Task<(Guid instanceId, Guid failedExecutionId)> SeedFailedWorkflowAsync(
        string? retryPolicyJson = null)
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var executionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = _fixture.CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Manual Retry Test Tenant",
            CreatedAt = now
        });

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            WorkflowDefinitionKey = "retry-test-workflow",
            Status = WorkflowInstanceStatus.Failed,
            CompletedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        });

        db.WorkflowStepExecutions.Add(new WorkflowStepExecution
        {
            Id = executionId,
            WorkflowInstanceId = instanceId,
            StepKey = "call-api",
            StepOrder = 1,
            StepType = "HttpRequest",
            Status = WorkflowStepExecutionStatus.Failed,
            FailureClassification = "TransientFailure",
            Attempt = 3,
            Error = "Connection refused after 3 attempts",
            Input = """{"url":"https://api.example.com"}""",
            RetryPolicyJson = retryPolicyJson,
            ScheduledAt = now.AddMinutes(-5),
            CompletedAt = now.AddMinutes(-1),
            CreatedAt = now.AddMinutes(-5),
            UpdatedAt = now.AddMinutes(-1)
        });

        db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instanceId,
            StepExecutionId = executionId,
            EventType = WorkflowEventTypes.StepFailed,
            CreatedAt = now.AddMinutes(-1)
        });

        db.WorkflowEvents.Add(new WorkflowEvent
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instanceId,
            EventType = WorkflowEventTypes.WorkflowFailed,
            CreatedAt = now.AddMinutes(-1)
        });

        await db.SaveChangesAsync();
        return (instanceId, executionId);
    }

    private async Task<Guid> SeedWorkflowWithStatusAsync(WorkflowInstanceStatus status)
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var db = _fixture.CreateDbContext();

        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Manual Retry Test Tenant",
            CreatedAt = now
        });

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            WorkflowDefinitionKey = "retry-test-workflow",
            Status = status,
            CompletedAt = status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed
                ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return instanceId;
    }
}
