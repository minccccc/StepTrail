using Microsoft.EntityFrameworkCore;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Tests.Infrastructure;
using Xunit;

namespace StepTrail.Shared.Tests.Integration;

[Collection(PostgresWorkflowDefinitionRepositoryCollection.Name)]
public class WorkflowDefinitionRepositoryIntegrationTests
{
    private readonly PostgresWorkflowDefinitionRepositoryFixture _fixture;

    public WorkflowDefinitionRepositoryIntegrationTests(PostgresWorkflowDefinitionRepositoryFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CreateDraftAsync_AndGetByIdAsync_RoundTripsWorkflowDefinition()
    {
        await _fixture.ResetAsync();

        var definition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("customer-created")),
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
                    new SendWebhookStepConfiguration("https://hooks.example.com/customer-created"),
                    retryPolicyOverrideKey: "partner-webhook-policy")
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(definition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedDefinition = await repository.GetByIdAsync(definition.Id);

            Assert.NotNull(loadedDefinition);
            Assert.Equal(definition.Id, loadedDefinition!.Id);
            Assert.Equal("customer-sync", loadedDefinition.Key);
            Assert.Equal(WorkflowDefinitionStatus.Draft, loadedDefinition.Status);
            Assert.Equal(TriggerType.Webhook, loadedDefinition.TriggerDefinition.Type);
            Assert.Equal("customer-created", loadedDefinition.TriggerDefinition.WebhookConfiguration!.RouteKey);
            Assert.Collection(
                loadedDefinition.StepDefinitions,
                step =>
                {
                    Assert.Equal("fetch-customer", step.Key);
                    Assert.Equal(StepType.HttpRequest, step.Type);
                },
                step =>
                {
                    Assert.Equal("shape-payload", step.Key);
                    Assert.Equal(StepType.Transform, step.Type);
                },
                step =>
                {
                    Assert.Equal("notify-partner", step.Key);
                    Assert.Equal("partner-webhook-policy", step.RetryPolicyOverrideKey);
                    Assert.Equal(StepType.SendWebhook, step.Type);
                });
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsUpdatedDefinitionState()
    {
        await _fixture.ResetAsync();

        var originalDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-before-processing",
                    1,
                    new DelayStepConfiguration(15))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(originalDefinition);
        }

        var updatedDefinition = new WorkflowDefinition(
            originalDefinition.Id,
            originalDefinition.Key,
            "Customer Sync Updated",
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
            [
                StepDefinition.CreateConditional(
                    Guid.NewGuid(),
                    "check-ready",
                    1,
                    new ConditionalStepConfiguration("payload.isReady == true")),
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    2,
                    new SendWebhookStepConfiguration("https://hooks.example.com/customer-updated"))
            ],
            originalDefinition.CreatedAtUtc,
            originalDefinition.UpdatedAtUtc.AddMinutes(30),
            "Updated executable workflow definition");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.UpdateAsync(updatedDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedDefinition = await repository.GetByIdAsync(originalDefinition.Id);

            Assert.NotNull(loadedDefinition);
            Assert.Equal("Customer Sync Updated", loadedDefinition!.Name);
            Assert.Equal(WorkflowDefinitionStatus.Active, loadedDefinition.Status);
            Assert.Equal(TriggerType.Api, loadedDefinition.TriggerDefinition.Type);
            Assert.Equal("start-customer-sync", loadedDefinition.TriggerDefinition.ApiConfiguration!.OperationKey);
            Assert.Collection(
                loadedDefinition.StepDefinitions,
                step => Assert.Equal(StepType.Conditional, step.Type),
                step => Assert.Equal(StepType.SendWebhook, step.Type));
        }
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenExistingDefinitionIsNotDraft()
    {
        await _fixture.ResetAsync();

        var activeDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-before-processing",
                    1,
                    new DelayStepConfiguration(15))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(activeDefinition);
        }

        var attemptedUpdate = new WorkflowDefinition(
            activeDefinition.Id,
            activeDefinition.Key,
            "Customer Sync Updated",
            activeDefinition.Version,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
            [
                StepDefinition.CreateConditional(
                    Guid.NewGuid(),
                    "check-ready",
                    1,
                    new ConditionalStepConfiguration("payload.isReady == true"))
            ],
            activeDefinition.CreatedAtUtc,
            activeDefinition.UpdatedAtUtc.AddMinutes(30),
            "Updated executable workflow definition");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(attemptedUpdate));

            Assert.Contains("can no longer be updated in place", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenDraftDefinitionChangesVersion()
    {
        await _fixture.ResetAsync();

        var draftDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-before-processing",
                    1,
                    new DelayStepConfiguration(15))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(draftDefinition);
        }

        var attemptedUpdate = new WorkflowDefinition(
            draftDefinition.Id,
            draftDefinition.Key,
            draftDefinition.Name,
            2,
            WorkflowDefinitionStatus.Draft,
            draftDefinition.TriggerDefinition,
            draftDefinition.StepDefinitions,
            draftDefinition.CreatedAtUtc,
            draftDefinition.UpdatedAtUtc.AddMinutes(10),
            draftDefinition.Description);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(attemptedUpdate));

            Assert.Contains("cannot change its version", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task UpdateAsync_Throws_WhenDraftDefinitionChangesKey()
    {
        await _fixture.ResetAsync();

        var draftDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-before-processing",
                    1,
                    new DelayStepConfiguration(15))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(draftDefinition);
        }

        var attemptedUpdate = new WorkflowDefinition(
            draftDefinition.Id,
            "customer-sync-updated",
            draftDefinition.Name,
            draftDefinition.Version,
            WorkflowDefinitionStatus.Draft,
            draftDefinition.TriggerDefinition,
            draftDefinition.StepDefinitions,
            draftDefinition.CreatedAtUtc,
            draftDefinition.UpdatedAtUtc.AddMinutes(10),
            draftDefinition.Description);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.UpdateAsync(attemptedUpdate));

            Assert.Contains("cannot change its key", ex.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_AndGetActiveByKeyAsync_PersistsMultipleVersions()
    {
        await _fixture.ResetAsync();

        var draftDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
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
            triggerDefinition: TriggerDefinition.CreateSchedule(
                Guid.NewGuid(),
                new ScheduleTriggerConfiguration(300)),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-upstream",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/sync")),
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "transform-response",
                    2,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("$.result", "$.payload")
                    ]))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.CreateDraftAsync(draftDefinition);
            await repository.SaveNewVersionAsync(activeDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedActiveDefinition = await repository.GetActiveByKeyAsync("customer-sync");

            Assert.NotNull(loadedActiveDefinition);
            Assert.Equal(activeDefinition.Id, loadedActiveDefinition!.Id);
            Assert.Equal(2, loadedActiveDefinition.Version);
            Assert.Equal(WorkflowDefinitionStatus.Active, loadedActiveDefinition.Status);
            Assert.Equal(TriggerType.Schedule, loadedActiveDefinition.TriggerDefinition.Type);
            Assert.Equal(300, loadedActiveDefinition.TriggerDefinition.ScheduleConfiguration!.IntervalSeconds);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_DeactivatesPreviouslyActiveVersion_ForSameWorkflowKey()
    {
        await _fixture.ResetAsync();

        var originalActiveDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait",
                    1,
                    new DelayStepConfiguration(5))
            ]);

        var replacementActiveDefinition = CreateWorkflowDefinition(
            version: 2,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateSchedule(
                Guid.NewGuid(),
                new ScheduleTriggerConfiguration(300)),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-upstream",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/sync"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(originalActiveDefinition);
            await repository.SaveNewVersionAsync(replacementActiveDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedOriginalDefinition = await repository.GetByIdAsync(originalActiveDefinition.Id);
            var loadedReplacementDefinition = await repository.GetByIdAsync(replacementActiveDefinition.Id);
            var loadedActiveDefinition = await repository.GetActiveByKeyAsync("customer-sync");

            Assert.NotNull(loadedOriginalDefinition);
            Assert.NotNull(loadedReplacementDefinition);
            Assert.NotNull(loadedActiveDefinition);
            Assert.Equal(WorkflowDefinitionStatus.Inactive, loadedOriginalDefinition!.Status);
            Assert.Equal(WorkflowDefinitionStatus.Active, loadedReplacementDefinition!.Status);
            Assert.Equal(replacementActiveDefinition.Id, loadedActiveDefinition!.Id);
            Assert.Equal(2, loadedActiveDefinition.Version);
        }
    }

    [Fact]
    public async Task UpdateAsync_WhenPromotingDefinitionToActive_DeactivatesPreviouslyActiveVersion()
    {
        await _fixture.ResetAsync();

        var originalActiveDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait",
                    1,
                    new DelayStepConfiguration(5))
            ]);

        var draftDefinition = CreateWorkflowDefinition(
            version: 2,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateApi(
                Guid.NewGuid(),
                new ApiTriggerConfiguration("start-customer-sync")),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-upstream",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/sync"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(originalActiveDefinition);
            await repository.CreateDraftAsync(draftDefinition);
        }

        var promotedDefinition = new WorkflowDefinition(
            draftDefinition.Id,
            draftDefinition.Key,
            draftDefinition.Name,
            draftDefinition.Version,
            WorkflowDefinitionStatus.Active,
            draftDefinition.TriggerDefinition,
            draftDefinition.StepDefinitions,
            draftDefinition.CreatedAtUtc,
            draftDefinition.UpdatedAtUtc.AddMinutes(10),
            draftDefinition.Description);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.UpdateAsync(promotedDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedOriginalDefinition = await repository.GetByIdAsync(originalActiveDefinition.Id);
            var loadedPromotedDefinition = await repository.GetByIdAsync(draftDefinition.Id);
            var loadedActiveDefinition = await repository.GetActiveByKeyAsync("customer-sync");

            Assert.NotNull(loadedOriginalDefinition);
            Assert.NotNull(loadedPromotedDefinition);
            Assert.NotNull(loadedActiveDefinition);
            Assert.Equal(WorkflowDefinitionStatus.Inactive, loadedOriginalDefinition!.Status);
            Assert.Equal(WorkflowDefinitionStatus.Active, loadedPromotedDefinition!.Status);
            Assert.Equal(draftDefinition.Id, loadedActiveDefinition!.Id);
            Assert.Equal(2, loadedActiveDefinition.Version);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_Throws_WhenActiveDefinitionFailsActivationValidation()
    {
        await _fixture.ResetAsync();

        var invalidActiveDefinition = CreateWorkflowDefinition(
            version: 3,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/customers")),
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    3,
                    new SendWebhookStepConfiguration("https://hooks.example.com/customer-created"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);

            var ex = await Assert.ThrowsAsync<WorkflowDefinitionValidationException>(() =>
                repository.SaveNewVersionAsync(invalidActiveDefinition));

            var error = Assert.Single(ex.ValidationResult.Errors);
            Assert.Equal("workflow.steps.order.sequence.invalid", error.Code);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            Assert.Empty(await dbContext.ExecutableWorkflowDefinitions.ToListAsync());
        }
    }

    private static WorkflowDefinition CreateWorkflowDefinition(
        int version,
        WorkflowDefinitionStatus status,
        TriggerDefinition triggerDefinition,
        IReadOnlyList<StepDefinition> stepDefinitions)
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddMinutes(version);

        return new WorkflowDefinition(
            Guid.NewGuid(),
            "customer-sync",
            "Customer Sync",
            version,
            status,
            triggerDefinition,
            stepDefinitions,
            createdAtUtc,
            updatedAtUtc,
            "Executable workflow definition used for persistence tests.");
    }
}
