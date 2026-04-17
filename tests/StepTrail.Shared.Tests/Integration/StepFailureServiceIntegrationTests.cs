using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Tests.Infrastructure;
using StepTrail.Shared.Workflows;
using StepTrail.Worker;
using StepTrail.Worker.Alerts;
using Xunit;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class StepFailureServiceIntegrationTests
{
    private static readonly RetryPolicy ThreeAttemptsFixed = new(
        maxAttempts: 3,
        initialDelaySeconds: 10,
        backoffStrategy: BackoffStrategy.Fixed,
        retryOnTimeout: true);

    private static readonly RetryPolicy FiveAttemptsFixed = new(
        maxAttempts: 5,
        initialDelaySeconds: 10,
        backoffStrategy: BackoffStrategy.Fixed,
        retryOnTimeout: true);

    private static readonly RetryPolicy ThreeAttemptsNoTimeout = new(
        maxAttempts: 3,
        initialDelaySeconds: 10,
        backoffStrategy: BackoffStrategy.Fixed,
        retryOnTimeout: false);

    private static readonly RetryPolicy ThreeAttemptsExponential = new(
        maxAttempts: 3,
        initialDelaySeconds: 5,
        backoffStrategy: BackoffStrategy.Exponential,
        maxDelaySeconds: 60);

    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public StepFailureServiceIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task HandleAsync_TransientFailure_SchedulesRetryWhenAttemptsRemain()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Connection refused",
            WorkflowEventTypes.StepFailed,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            failureClassification: StepExecutionFailureClassification.TransientFailure);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.NotNull(retryExecution);
        Assert.Equal(WorkflowStepExecutionStatus.Pending, retryExecution!.Status);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.AwaitingRetry, instance!.Status);

        Assert.Equal(WorkflowStepExecutionStatus.Failed, execution.Status);
        Assert.Equal("TransientFailure", execution.FailureClassification);
    }

    [Fact]
    public async Task HandleAsync_PermanentFailure_SkipsRetryAndFailsWorkflow()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Invalid response format",
            WorkflowEventTypes.StepFailed,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            failureClassification: StepExecutionFailureClassification.PermanentFailure);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.Null(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Failed, instance!.Status);

        Assert.Equal("PermanentFailure", execution.FailureClassification);
    }

    [Fact]
    public async Task HandleAsync_InvalidConfiguration_SkipsRetryAndFailsWorkflow()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Missing webhook URL",
            WorkflowEventTypes.StepFailed,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            failureClassification: StepExecutionFailureClassification.InvalidConfiguration);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.Null(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Failed, instance!.Status);

        Assert.Equal("InvalidConfiguration", execution.FailureClassification);
    }

    [Fact]
    public async Task HandleAsync_InputResolutionFailure_SkipsRetryAndFailsWorkflow()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Placeholder 'input.customerId' could not be resolved",
            WorkflowEventTypes.StepFailed,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            failureClassification: StepExecutionFailureClassification.InputResolutionFailure);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.Null(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Failed, instance!.Status);

        Assert.Equal("InputResolutionFailure", execution.FailureClassification);
    }

    [Fact]
    public async Task HandleAsync_NullClassification_SchedulesRetryWhenAttemptsRemain()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Worker crashed mid-execution",
            WorkflowEventTypes.StepOrphaned,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            failureClassification: null);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.NotNull(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.AwaitingRetry, instance!.Status);

        Assert.Null(execution.FailureClassification);
    }

    [Fact]
    public async Task HandleAsync_TransientFailure_FailsWorkflowWhenRetriesExhausted()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 3);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Connection refused",
            WorkflowEventTypes.StepFailed,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            failureClassification: StepExecutionFailureClassification.TransientFailure);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 4);
        Assert.Null(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Failed, instance!.Status);
    }

    [Fact]
    public async Task HandleAsync_PermanentFailure_SkipsRetryEvenOnFirstAttempt()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "400 Bad Request",
            WorkflowEventTypes.StepFailed,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: FiveAttemptsFixed,
            failureClassification: StepExecutionFailureClassification.PermanentFailure);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.Null(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Failed, instance!.Status);
    }

    [Fact]
    public async Task HandleAsync_Timeout_SkipsRetryWhenRetryOnTimeoutIsFalse()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Step timed out after 30s",
            WorkflowEventTypes.StepTimedOut,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsNoTimeout,
            isTimeout: true);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.Null(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Failed, instance!.Status);
    }

    [Fact]
    public async Task HandleAsync_Timeout_RetriesWhenRetryOnTimeoutIsTrue()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 1);

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Step timed out after 30s",
            WorkflowEventTypes.StepTimedOut,
            DateTimeOffset.UtcNow,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsFixed,
            isTimeout: true);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 2);
        Assert.NotNull(retryExecution);

        var instance = await FindInstanceAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.AwaitingRetry, instance!.Status);
    }

    [Fact]
    public async Task HandleAsync_ExponentialBackoff_ComputesCorrectRetryDelay()
    {
        await _fixture.ResetAsync();
        var (_, instanceId) = await SeedWorkflowInstanceAsync();
        var execution = await SeedStepExecutionAsync(instanceId, attempt: 2);

        var now = DateTimeOffset.UtcNow;

        await using var db = _fixture.CreateDbContext();
        db.Attach(execution);

        var service = CreateService(db);

        await service.HandleAsync(
            execution,
            "Connection refused",
            WorkflowEventTypes.StepFailed,
            now,
            CancellationToken.None,
            retryPolicy: ThreeAttemptsExponential,
            failureClassification: StepExecutionFailureClassification.TransientFailure);

        var retryExecution = await FindRetryExecutionAsync(instanceId, execution.StepKey, attempt: 3);
        Assert.NotNull(retryExecution);

        // Attempt 2 failed → exponential delay = 5 * 2^(2-1) = 10s
        var expectedRetryAt = now.AddSeconds(10);
        Assert.True(
            Math.Abs((retryExecution!.ScheduledAt - expectedRetryAt).TotalSeconds) < 1,
            $"Expected retry at ~{expectedRetryAt:O} but was {retryExecution.ScheduledAt:O}");
    }

    private StepFailureService CreateService(StepTrailDbContext db) =>
        new(db,
            new AlertService([], db, NullLogger<AlertService>.Instance),
            AlertRuleEvaluator.CreateDefault(),
            new StepTrail.Shared.AuditLog.AuditLogService(db, NullLogger<StepTrail.Shared.AuditLog.AuditLogService>.Instance),
            NullLogger<StepFailureService>.Instance);

    private async Task<(Guid tenantId, Guid instanceId)> SeedWorkflowInstanceAsync()
    {
        var tenantId = Guid.NewGuid();
        var instanceId = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Failure Classification Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            WorkflowDefinitionKey = "test-workflow",
            Status = WorkflowInstanceStatus.Running,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        return (tenantId, instanceId);
    }

    private async Task<WorkflowStepExecution> SeedStepExecutionAsync(Guid instanceId, int attempt)
    {
        var execution = new WorkflowStepExecution
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = instanceId,
            StepKey = "call-partner-api",
            StepOrder = 1,
            StepType = "HttpRequest",
            Status = WorkflowStepExecutionStatus.Running,
            Attempt = attempt,
            ScheduledAt = DateTimeOffset.UtcNow.AddSeconds(-5),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await using var db = _fixture.CreateDbContext();
        db.WorkflowStepExecutions.Add(execution);
        await db.SaveChangesAsync();

        return execution;
    }

    private async Task<WorkflowStepExecution?> FindRetryExecutionAsync(Guid instanceId, string stepKey, int attempt)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.WorkflowStepExecutions
            .AsQueryable()
            .Where(e => e.WorkflowInstanceId == instanceId
                     && e.StepKey == stepKey
                     && e.Attempt == attempt)
            .FirstOrDefaultAsync();
    }

    private async Task<WorkflowInstance?> FindInstanceAsync(Guid instanceId)
    {
        await using var db = _fixture.CreateDbContext();
        return await db.WorkflowInstances.FindAsync(instanceId);
    }
}
