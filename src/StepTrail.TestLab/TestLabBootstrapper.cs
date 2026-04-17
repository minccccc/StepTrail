using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Entities;
using ExecutableWorkflowDefinition = StepTrail.Shared.Definitions.WorkflowDefinition;

namespace StepTrail.TestLab;

public sealed class TestLabBootstrapper
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<TestLabOptions> _options;
    private readonly LabStateStore _stateStore;
    private readonly ILogger<TestLabBootstrapper> _logger;

    public TestLabBootstrapper(
        IServiceScopeFactory scopeFactory,
        IOptions<TestLabOptions> options,
        LabStateStore stateStore,
        ILogger<TestLabBootstrapper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _stateStore = stateStore;
        _logger = logger;
    }

    public async Task<string> EnsureDemoAssetsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();
        var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();

        var publicBaseUrl = _options.Value.PublicBaseUrl.TrimEnd('/');
        await UpsertSecretAsync(db, TestLabDefaults.ApiAUrlSecret, $"{publicBaseUrl}/mock/api-a", "TestLab mock endpoint for API A.", ct);
        await UpsertSecretAsync(db, TestLabDefaults.ApiBUrlSecret, $"{publicBaseUrl}/mock/api-b", "TestLab mock endpoint for API B.", ct);
        await UpsertSecretAsync(db, TestLabDefaults.ApiATokenSecret, "testlab-api-a-token", "Bearer token used by the demo workflow for API A.", ct);
        await UpsertSecretAsync(db, TestLabDefaults.ApiBTokenSecret, "testlab-api-b-token", "Bearer token used by the demo workflow for API B.", ct);

        var definition = await repository.GetActiveByKeyAsync(TestLabDefaults.WorkflowKey, ct)
            ?? await repository.GetByKeyAndVersionAsync(TestLabDefaults.WorkflowKey, 1, ct);

        if (definition is null)
        {
            definition = await repository.SaveNewVersionAsync(CreateDemoWorkflowDefinition(), ct);
            _logger.LogWarning("Created TestLab workflow definition '{WorkflowKey}' v{Version}.", definition.Key, definition.Version);
        }

        if (definition.Status != WorkflowDefinitionStatus.Active)
        {
            definition = await repository.UpdateAsync(new ExecutableWorkflowDefinition(
                definition.Id,
                definition.Key,
                definition.Name,
                definition.Version,
                WorkflowDefinitionStatus.Active,
                definition.TriggerDefinition,
                definition.StepDefinitions,
                definition.CreatedAtUtc,
                DateTimeOffset.UtcNow,
                definition.Description,
                definition.SourceTemplateKey,
                definition.SourceTemplateVersion), ct);
        }

        var status = $"Workflow '{definition.Key}' is ready on route '/webhooks/{TestLabDefaults.WorkflowRouteKey}'.";
        _stateStore.MarkDemoWorkflowReady(status);
        return status;
    }

    private static ExecutableWorkflowDefinition CreateDemoWorkflowDefinition()
    {
        var now = DateTimeOffset.UtcNow;

        return new ExecutableWorkflowDefinition(
            Guid.NewGuid(),
            TestLabDefaults.WorkflowKey,
            TestLabDefaults.WorkflowName,
            1,
            WorkflowDefinitionStatus.Active,
            TriggerDefinition.CreateWebhook(
                Guid.NewGuid(),
                new WebhookTriggerConfiguration(TestLabDefaults.WorkflowRouteKey)),
            [
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "transform-for-api-a",
                    1,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("requestId", "{{input.id}}"),
                        new TransformValueMapping("action", "{{input.action}}"),
                        new TransformValueMapping("customerId", "{{input.payload.customerId}}"),
                        new TransformValueMapping("scenario", "{{input.labScenario}}")
                    ])),
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-api-a",
                    2,
                    new HttpRequestStepConfiguration(
                        $"{{{{secrets.{TestLabDefaults.ApiAUrlSecret}}}}}",
                        "POST",
                        new Dictionary<string, string>
                        {
                            ["Authorization"] = $"Bearer {{{{secrets.{TestLabDefaults.ApiATokenSecret}}}}}"
                        },
                        timeoutSeconds: 20),
                    retryPolicy: new RetryPolicy(2, 2, BackoffStrategy.Fixed)),
                StepDefinition.CreateTransform(
                    Guid.NewGuid(),
                    "transform-for-api-b",
                    3,
                    new TransformStepConfiguration(
                    [
                        new TransformValueMapping("sourceId", "{{steps.call-api-a.output.body.id}}"),
                        new TransformValueMapping("status", "{{steps.call-api-a.output.body.status}}"),
                        new TransformValueMapping("scenario", "{{steps.transform-for-api-a.output.scenario}}"),
                        new TransformValueMapping("originalRequestId", "{{steps.transform-for-api-a.output.requestId}}"),
                        new TransformValueMapping("customerId", "{{steps.transform-for-api-a.output.customerId}}")
                    ])),
                StepDefinition.CreateHttpRequest(
                    Guid.NewGuid(),
                    "call-api-b",
                    4,
                    new HttpRequestStepConfiguration(
                        $"{{{{secrets.{TestLabDefaults.ApiBUrlSecret}}}}}",
                        "POST",
                        new Dictionary<string, string>
                        {
                            ["Authorization"] = $"Bearer {{{{secrets.{TestLabDefaults.ApiBTokenSecret}}}}}"
                        },
                        timeoutSeconds: 20),
                    retryPolicy: new RetryPolicy(3, 4, BackoffStrategy.Fixed))
            ],
            now,
            now,
            description: "Simple TestLab workflow for demos. Shows success, retry, and permanent failure without changing the core services.",
            sourceTemplateKey: TestLabDefaults.SourceTemplateKey,
            sourceTemplateVersion: 1);
    }

    private static async Task UpsertSecretAsync(
        StepTrailDbContext db,
        string name,
        string value,
        string description,
        CancellationToken ct)
    {
        var existing = await db.WorkflowSecrets
            .FirstOrDefaultAsync(secret => secret.Name == name, ct);

        if (existing is null)
        {
            db.WorkflowSecrets.Add(new WorkflowSecret
            {
                Id = Guid.NewGuid(),
                Name = name,
                Value = value,
                Description = description,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existing.Value = value;
            existing.Description = description;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
