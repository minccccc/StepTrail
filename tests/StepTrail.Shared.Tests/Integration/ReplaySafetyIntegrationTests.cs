using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StepTrail.Api.Services;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Entities;
using StepTrail.Shared.Runtime;
using StepTrail.Shared.Tests.Infrastructure;
using StepTrail.Shared.Workflows;
using Xunit;
using ExecutableWorkflowDefinition = StepTrail.Shared.Definitions.WorkflowDefinition;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class ReplaySafetyIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public ReplaySafetyIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ReplayAsync_FailedWorkflow_CreatesNewExecutionsAndMovesToRunning()
    {
        await _fixture.ResetAsync();
        var (instanceId, definition) = await SeedFailedWorkflowWithDefinitionAsync();

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var response = await service.ReplayAsync(instanceId, CancellationToken.None);

        Assert.Equal(instanceId, response.InstanceId);
        Assert.Equal("Running", response.InstanceStatus);

        // Verify workflow is Running with CompletedAt cleared
        var instance = await db.WorkflowInstances.FindAsync(instanceId);
        Assert.Equal(WorkflowInstanceStatus.Running, instance!.Status);
        Assert.Null(instance.CompletedAt);

        // Verify new step executions were created (preserving old ones)
        var executions = await db.WorkflowStepExecutions
            .Where(e => e.WorkflowInstanceId == instanceId)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync();

        // Old executions (1 failed) + new executions (all steps replayed)
        Assert.True(executions.Count > 1);

        var newFirstStep = executions
            .Where(e => e.Id == response.NewStepExecutionId)
            .Single();

        Assert.Equal(WorkflowStepExecutionStatus.Pending, newFirstStep.Status);
        Assert.Equal(1, newFirstStep.Attempt);
    }

    [Fact]
    public async Task ReplayAsync_FailedWorkflow_EventPayloadContainsReplayMetadata()
    {
        await _fixture.ResetAsync();
        var (instanceId, _) = await SeedFailedWorkflowWithDefinitionAsync();

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        await service.ReplayAsync(instanceId, CancellationToken.None);

        var replayEvent = await db.WorkflowEvents
            .Where(e => e.WorkflowInstanceId == instanceId
                     && e.EventType == WorkflowEventTypes.WorkflowReplayed)
            .FirstOrDefaultAsync();

        Assert.NotNull(replayEvent);
        Assert.NotNull(replayEvent!.Payload);

        using var payload = JsonDocument.Parse(replayEvent.Payload!);
        Assert.Equal("manual", payload.RootElement.GetProperty("origin").GetString());
        Assert.Equal("Failed", payload.RootElement.GetProperty("previousStatus").GetString());
        Assert.True(payload.RootElement.GetProperty("priorExecutionCount").GetInt32() >= 1);
        Assert.True(payload.RootElement.GetProperty("newStepCount").GetInt32() >= 1);
        Assert.True(payload.RootElement.TryGetProperty("definitionVersion", out _));
    }

    [Fact]
    public async Task ReplayAsync_CompletedWorkflow_IsAllowed()
    {
        await _fixture.ResetAsync();
        var (instanceId, _) = await SeedCompletedWorkflowWithDefinitionAsync();

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var response = await service.ReplayAsync(instanceId, CancellationToken.None);

        Assert.Equal("Running", response.InstanceStatus);
    }

    [Fact]
    public async Task ReplayAsync_RunningWorkflow_Throws()
    {
        await _fixture.ResetAsync();
        var instanceId = await SeedWorkflowWithStatusAsync(WorkflowInstanceStatus.Running);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.ReplayAsync(instanceId, CancellationToken.None));

        Assert.Contains("Running", ex.Message);
    }

    [Fact]
    public async Task ReplayAsync_AwaitingRetryWorkflow_Throws()
    {
        await _fixture.ResetAsync();
        var instanceId = await SeedWorkflowWithStatusAsync(WorkflowInstanceStatus.AwaitingRetry);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.ReplayAsync(instanceId, CancellationToken.None));

        Assert.Contains("AwaitingRetry", ex.Message);
    }

    [Fact]
    public async Task ReplayAsync_CancelledWorkflow_Throws()
    {
        await _fixture.ResetAsync();
        var instanceId = await SeedWorkflowWithStatusAsync(WorkflowInstanceStatus.Cancelled);

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
            service.ReplayAsync(instanceId, CancellationToken.None));

        Assert.Contains("Cancelled", ex.Message);
    }

    [Fact]
    public async Task ReplayAsync_VersionMismatch_Throws()
    {
        await _fixture.ResetAsync();

        var tenantId = await CreateTenantAsync();
        var definition = CreateDefinition(version: 1);

        Guid instanceId;

        await using (var db = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(db);
            await repository.SaveNewVersionAsync(definition);

            var startService = new WorkflowStartService(
                db,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var result = await startService.StartAsync(new WorkflowStartRequest
            {
                WorkflowKey = definition.Key,
                TenantId = tenantId,
                Input = new { value = "test" }
            });

            instanceId = result.Id;
        }

        // Mark workflow as Failed so replay is allowed status-wise
        await using (var db = _fixture.CreateDbContext())
        {
            var instance = await db.WorkflowInstances.FindAsync(instanceId);
            instance!.Status = WorkflowInstanceStatus.Failed;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Simulate a definition version change by updating the instance's stored version
        // to differ from the definition's actual version.
        await using (var db = _fixture.CreateDbContext())
        {
            var instance = await db.WorkflowInstances.FindAsync(instanceId);
            instance!.WorkflowDefinitionVersion = 99;
            instance.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        // Attempt replay — should fail because version 99 != definition version 1
        await using (var db = _fixture.CreateDbContext())
        {
            var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

            var ex = await Assert.ThrowsAsync<InvalidWorkflowStateException>(() =>
                service.ReplayAsync(instanceId, CancellationToken.None));

            Assert.Contains("version", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task ReplayAsync_NonExistentWorkflow_Throws()
    {
        await _fixture.ResetAsync();

        await using var db = _fixture.CreateDbContext();
        var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));

        await Assert.ThrowsAsync<WorkflowInstanceNotFoundException>(() =>
            service.ReplayAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task ReplayAsync_PreservesOriginalExecutionHistory()
    {
        await _fixture.ResetAsync();
        var (instanceId, _) = await SeedFailedWorkflowWithDefinitionAsync();

        Guid originalExecutionId;
        await using (var db = _fixture.CreateDbContext())
        {
            var original = await db.WorkflowStepExecutions
                .Where(e => e.WorkflowInstanceId == instanceId)
                .FirstAsync();
            originalExecutionId = original.Id;
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var service = new WorkflowRetryService(db, new WorkflowDefinitionRepository(db));
            await service.ReplayAsync(instanceId, CancellationToken.None);
        }

        // Verify original execution is still there, untouched
        await using (var db = _fixture.CreateDbContext())
        {
            var original = await db.WorkflowStepExecutions.FindAsync(originalExecutionId);
            Assert.NotNull(original);
            Assert.Equal(WorkflowStepExecutionStatus.Failed, original!.Status);
        }
    }

    private async Task<Guid> CreateTenantAsync()
    {
        var tenantId = Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Name = "Replay Safety Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        return tenantId;
    }

    private async Task<(Guid instanceId, ExecutableWorkflowDefinition definition)> SeedFailedWorkflowWithDefinitionAsync()
    {
        var tenantId = await CreateTenantAsync();
        var definition = CreateDefinition(version: 1);

        Guid instanceId;

        await using (var db = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(db);
            await repository.SaveNewVersionAsync(definition);

            var startService = new WorkflowStartService(
                db,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var result = await startService.StartAsync(new WorkflowStartRequest
            {
                WorkflowKey = definition.Key,
                TenantId = tenantId,
                Input = new { value = "test" }
            });

            instanceId = result.Id;
        }

        // Mark the instance and first step as Failed
        await using (var db = _fixture.CreateDbContext())
        {
            var instance = await db.WorkflowInstances.FindAsync(instanceId);
            instance!.Status = WorkflowInstanceStatus.Failed;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            instance.UpdatedAt = DateTimeOffset.UtcNow;

            var firstStep = await db.WorkflowStepExecutions
                .Where(e => e.WorkflowInstanceId == instanceId)
                .OrderBy(e => e.StepOrder)
                .FirstAsync();

            firstStep.Status = WorkflowStepExecutionStatus.Failed;
            firstStep.Error = "Simulated failure for replay test";
            firstStep.FailureClassification = "TransientFailure";
            firstStep.CompletedAt = DateTimeOffset.UtcNow;
            firstStep.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync();
        }

        return (instanceId, definition);
    }

    private async Task<(Guid instanceId, ExecutableWorkflowDefinition definition)> SeedCompletedWorkflowWithDefinitionAsync()
    {
        var tenantId = await CreateTenantAsync();
        var definition = CreateDefinition(version: 1);

        Guid instanceId;

        await using (var db = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(db);
            await repository.SaveNewVersionAsync(definition);

            var startService = new WorkflowStartService(
                db,
                new WorkflowRegistry(Array.Empty<WorkflowDescriptor>()),
                repository);

            var result = await startService.StartAsync(new WorkflowStartRequest
            {
                WorkflowKey = definition.Key,
                TenantId = tenantId,
                Input = new { value = "test" }
            });

            instanceId = result.Id;
        }

        await using (var db = _fixture.CreateDbContext())
        {
            var instance = await db.WorkflowInstances.FindAsync(instanceId);
            instance!.Status = WorkflowInstanceStatus.Completed;
            instance.CompletedAt = DateTimeOffset.UtcNow;
            instance.UpdatedAt = DateTimeOffset.UtcNow;

            var steps = await db.WorkflowStepExecutions
                .Where(e => e.WorkflowInstanceId == instanceId)
                .ToListAsync();

            foreach (var step in steps)
            {
                step.Status = WorkflowStepExecutionStatus.Completed;
                step.CompletedAt = DateTimeOffset.UtcNow;
                step.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync();
        }

        return (instanceId, definition);
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
            Name = "Replay Safety Test Tenant",
            CreatedAt = now
        });

        db.WorkflowInstances.Add(new WorkflowInstance
        {
            Id = instanceId,
            TenantId = tenantId,
            WorkflowDefinitionKey = "replay-test",
            Status = status,
            CompletedAt = status is WorkflowInstanceStatus.Completed or WorkflowInstanceStatus.Failed
                ? now : null,
            CreatedAt = now,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();
        return instanceId;
    }

    private static ExecutableWorkflowDefinition CreateDefinition(int version)
    {
        var now = DateTimeOffset.UtcNow;

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            "replay-safety-test",
            "Replay Safety Test",
            version,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            [
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "transform-data",
                    1,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("result", "{{input.value}}")
                    ]))
            ],
            now,
            now.AddMinutes(1),
            "Test workflow for replay safety integration tests.");
    }
}
