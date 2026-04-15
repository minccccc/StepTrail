using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
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
                new WebhookTriggerConfiguration(
                    "customer-created",
                    "POST",
                    new WebhookSignatureValidationConfiguration(
                        "X-StepTrail-Signature",
                        "partner-signing-secret",
                        WebhookSignatureAlgorithm.HmacSha256,
                        "sha256="),
                    [
                        new WebhookInputMapping("eventId", "body.event_id"),
                        new WebhookInputMapping("requestId", "headers.x-request-id")
                    ],
                    new WebhookIdempotencyKeyExtractionConfiguration("headers.x-delivery-id"))),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "fetch-customer",
                    1,
                    new HttpRequestStepConfiguration(
                        "https://api.example.com/customers",
                        timeoutSeconds: 15,
                        responseClassification: new HttpResponseClassificationConfiguration(
                            retryableStatusCodes: [409, 429]))),
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "shape-payload",
                    2,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("$.customerId", "$.id"),
                        new TransformValueMapping(
                            "$.displayName",
                            TransformValueOperation.CreateFormatString(
                                "Customer {0}",
                                ["{{input.id}}"]))
                    ])),
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    3,
                    new SendWebhookStepConfiguration(
                        "https://hooks.example.com/customer-created",
                        timeoutSeconds: 10),
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
            Assert.NotNull(loadedDefinition.TriggerDefinition.WebhookConfiguration.SignatureValidation);
            Assert.Equal(
                "X-StepTrail-Signature",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.SignatureValidation!.HeaderName);
            Assert.Equal(
                "partner-signing-secret",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.SignatureValidation.SecretName);
            Assert.Equal(
                WebhookSignatureAlgorithm.HmacSha256,
                loadedDefinition.TriggerDefinition.WebhookConfiguration.SignatureValidation.Algorithm);
            Assert.Equal(
                "sha256=",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.SignatureValidation.SignaturePrefix);
            Assert.Equal(2, loadedDefinition.TriggerDefinition.WebhookConfiguration.InputMappings.Count);
            Assert.Equal(
                "eventId",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.InputMappings[0].TargetPath);
            Assert.Equal(
                "body.event_id",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.InputMappings[0].SourcePath);
            Assert.Equal(
                "requestId",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.InputMappings[1].TargetPath);
            Assert.Equal(
                "headers.x-request-id",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.InputMappings[1].SourcePath);
            Assert.NotNull(loadedDefinition.TriggerDefinition.WebhookConfiguration.IdempotencyKeyExtraction);
            Assert.Equal(
                "headers.x-delivery-id",
                loadedDefinition.TriggerDefinition.WebhookConfiguration.IdempotencyKeyExtraction!.SourcePath);
            Assert.Collection(
                loadedDefinition.StepDefinitions,
                step =>
                {
                    Assert.Equal("fetch-customer", step.Key);
                    Assert.Equal(StepType.HttpRequest, step.Type);
                    Assert.Equal(15, step.HttpRequestConfiguration!.TimeoutSeconds);
                    Assert.Equal([409, 429], step.HttpRequestConfiguration.ResponseClassification!.RetryableStatusCodes);
                },
                step =>
                {
                    Assert.Equal("shape-payload", step.Key);
                    Assert.Equal(StepType.Transform, step.Type);
                    Assert.Equal(2, step.TransformConfiguration!.Mappings.Count);
                    Assert.Equal("Customer {0}", step.TransformConfiguration.Mappings[1].Operation!.Template);
                    Assert.Equal(TransformOperationType.FormatString, step.TransformConfiguration.Mappings[1].Operation!.Type);
                },
                step =>
                {
                    Assert.Equal("notify-partner", step.Key);
                    Assert.Equal("partner-webhook-policy", step.RetryPolicyOverrideKey);
                    Assert.Equal(StepType.SendWebhook, step.Type);
                    Assert.Equal(10, step.SendWebhookConfiguration!.TimeoutSeconds);
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
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
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
            Assert.Equal(TriggerType.Manual, loadedDefinition.TriggerDefinition.Type);
            Assert.Equal("ops-console", loadedDefinition.TriggerDefinition.ManualConfiguration!.EntryPointKey);
            Assert.Collection(
                loadedDefinition.StepDefinitions,
                step =>
                {
                    Assert.Equal(StepType.Conditional, step.Type);
                    Assert.Equal("$.payload.isReady", step.ConditionalConfiguration!.SourcePath);
                    Assert.Equal(ConditionalOperator.Equals, step.ConditionalConfiguration.Operator);
                    Assert.Equal("true", step.ConditionalConfiguration.ExpectedValue);
                    Assert.Equal(ConditionalFalseOutcome.CompleteWorkflow, step.ConditionalConfiguration.FalseOutcome);
                },
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
            TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
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
        await SeedDefaultTenantAsync();

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
    public async Task SaveNewVersionAsync_WhenActiveScheduleTrigger_UpsertsExecutableRecurringSchedule()
    {
        await _fixture.ResetAsync();
        await SeedDefaultTenantAsync();

        var activeDefinition = CreateWorkflowDefinition(
            version: 1,
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
            await repository.SaveNewVersionAsync(activeDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();

            Assert.Null(schedule.WorkflowDefinitionId);
            Assert.Equal(activeDefinition.Key, schedule.ExecutableWorkflowKey);
            Assert.Equal(StepTrailRuntimeDefaults.DefaultTenantId, schedule.TenantId);
            Assert.Equal(300, schedule.IntervalSeconds);
            Assert.Null(schedule.CronExpression);
            Assert.True(schedule.IsEnabled);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_WhenActiveCronScheduleTrigger_UpsertsExecutableRecurringSchedule()
    {
        await _fixture.ResetAsync();
        await SeedDefaultTenantAsync();

        var activeDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateSchedule(
                Guid.NewGuid(),
                new ScheduleTriggerConfiguration("0 8 * * 1-5")),
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
            await repository.SaveNewVersionAsync(activeDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var loadedDefinition = await new WorkflowDefinitionRepository(dbContext)
                .GetActiveByKeyAsync(activeDefinition.Key);
            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();

            Assert.NotNull(loadedDefinition);
            Assert.Null(loadedDefinition!.TriggerDefinition.ScheduleConfiguration!.IntervalSeconds);
            Assert.Equal("0 8 * * 1-5", loadedDefinition.TriggerDefinition.ScheduleConfiguration.CronExpression);
            Assert.Null(schedule.IntervalSeconds);
            Assert.Equal("0 8 * * 1-5", schedule.CronExpression);
            Assert.True(schedule.NextRunAt > activeDefinition.UpdatedAtUtc);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_WhenNewActiveVersionIsNotSchedule_DisablesExecutableRecurringSchedule()
    {
        await _fixture.ResetAsync();
        await SeedDefaultTenantAsync();

        var scheduleDefinition = CreateWorkflowDefinition(
            version: 1,
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
        var manualDefinition = CreateWorkflowDefinition(
            version: 2,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
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
            await repository.SaveNewVersionAsync(scheduleDefinition);
            await repository.SaveNewVersionAsync(manualDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var schedule = await dbContext.RecurringWorkflowSchedules.SingleAsync();

            Assert.Equal(scheduleDefinition.Key, schedule.ExecutableWorkflowKey);
            Assert.False(schedule.IsEnabled);
        }
    }

    [Fact]
    public async Task GetActiveWebhookByRouteKeyAsync_ReturnsMatchingActiveWebhookDefinition()
    {
        await _fixture.ResetAsync();

        var webhookDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("partner-events")),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/forward"))
            ]);

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(webhookDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            var loadedDefinition = await repository.GetActiveWebhookByRouteKeyAsync("partner-events");

            Assert.NotNull(loadedDefinition);
            Assert.Equal(webhookDefinition.Id, loadedDefinition!.Id);
            Assert.Equal(TriggerType.Webhook, loadedDefinition.TriggerDefinition.Type);
            Assert.Equal("partner-events", loadedDefinition.TriggerDefinition.WebhookConfiguration!.RouteKey);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_AllowsReusingWebhookRouteKeyAcrossVersionsOfSameWorkflowKey()
    {
        await _fixture.ResetAsync();

        var originalActiveDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("partner-events")),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/forward"))
            ]);

        var replacementActiveDefinition = CreateWorkflowDefinition(
            version: 2,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("partner-events")),
            stepDefinitions:
            [
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    1,
                    new SendWebhookStepConfiguration("https://hooks.example.com/partner-events"))
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
            var loadedReplacementDefinition = await repository.GetActiveWebhookByRouteKeyAsync("partner-events");

            Assert.NotNull(loadedOriginalDefinition);
            Assert.NotNull(loadedReplacementDefinition);
            Assert.Equal(WorkflowDefinitionStatus.Inactive, loadedOriginalDefinition!.Status);
            Assert.Equal(replacementActiveDefinition.Id, loadedReplacementDefinition!.Id);
            Assert.Equal(2, loadedReplacementDefinition.Version);
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_Throws_WhenDifferentActiveWorkflowUsesSameWebhookRouteKey()
    {
        await _fixture.ResetAsync();

        var firstDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("partner-events")),
            stepDefinitions:
            [
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "forward-payload",
                    1,
                    new HttpRequestStepConfiguration("https://api.example.com/forward"))
            ],
            key: "customer-sync");

        var secondDefinition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Active,
            triggerDefinition: TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration("partner-events")),
            stepDefinitions:
            [
                StepDefinition.CreateSendWebhook(
                    Guid.NewGuid(),
                    "notify-partner",
                    1,
                    new SendWebhookStepConfiguration("https://hooks.example.com/partner-events"))
            ],
            key: "order-sync");

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);
            await repository.SaveNewVersionAsync(firstDefinition);
        }

        await using (var dbContext = _fixture.CreateDbContext())
        {
            var repository = new WorkflowDefinitionRepository(dbContext);

            await Assert.ThrowsAsync<DbUpdateException>(() => repository.SaveNewVersionAsync(secondDefinition));
        }
    }

    [Fact]
    public async Task SaveNewVersionAsync_DeactivatesPreviouslyActiveVersion_ForSameWorkflowKey()
    {
        await _fixture.ResetAsync();
        await SeedDefaultTenantAsync();

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
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
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

    [Fact]
    public async Task CreateDraftAsync_AndGetByIdAsync_RoundTripsDelayUntilConfiguration()
    {
        await _fixture.ResetAsync();

        var definition = CreateWorkflowDefinition(
            version: 1,
            status: WorkflowDefinitionStatus.Draft,
            triggerDefinition: TriggerDefinition.CreateManual(
                Guid.NewGuid(),
                new ManualTriggerConfiguration("ops-console")),
            stepDefinitions:
            [
                StepDefinition.CreateDelay(
                    Guid.NewGuid(),
                    "wait-until-follow-up",
                    1,
                    new DelayStepConfiguration("{{input.followUpAtUtc}}"))
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
            var delayStep = Assert.Single(loadedDefinition!.StepDefinitions);
            Assert.Equal(StepType.Delay, delayStep.Type);
            Assert.Null(delayStep.DelayConfiguration!.DelaySeconds);
            Assert.Equal("{{input.followUpAtUtc}}", delayStep.DelayConfiguration.TargetTimeExpression);
        }
    }

    private static WorkflowDefinition CreateWorkflowDefinition(
        int version,
        WorkflowDefinitionStatus status,
        TriggerDefinition triggerDefinition,
        IReadOnlyList<StepDefinition> stepDefinitions,
        string key = "customer-sync")
    {
        var createdAtUtc = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var updatedAtUtc = createdAtUtc.AddMinutes(version);

        return new WorkflowDefinition(
            Guid.NewGuid(),
            key,
            "Customer Sync",
            version,
            status,
            triggerDefinition,
            stepDefinitions,
            createdAtUtc,
            updatedAtUtc,
            "Executable workflow definition used for persistence tests.");
    }

    private async Task SeedDefaultTenantAsync()
    {
        await using var dbContext = _fixture.CreateDbContext();
        dbContext.Tenants.Add(new StepTrail.Shared.Entities.Tenant
        {
            Id = StepTrailRuntimeDefaults.DefaultTenantId,
            Name = "Default",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();
    }
}
