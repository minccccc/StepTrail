using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Definitions;
using StepTrail.Shared.Definitions.Persistence;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Services;

/// <summary>
/// Seeds representative workflow definitions and instances for local development.
/// Creates definitions from built-in templates, activates them, and creates
/// instances in various statuses so every UI state is visible immediately.
///
/// Every instance is properly associated with an executable workflow definition.
/// Idempotent — skips seeding if any seed instance is already present.
/// </summary>
public sealed class DevDataSeedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DevDataSeedService> _logger;

    private static readonly Guid TenantId = TenantSeedService.DefaultTenantId;

    public DevDataSeedService(IServiceScopeFactory scopeFactory, ILogger<DevDataSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionRepository>();

        if (await db.WorkflowInstances.AnyAsync(i => i.ExternalKey == "forward-ok", ct))
        {
            _logger.LogDebug("Dev seed data already present — skipping");
            return;
        }

        // ── Create and activate workflow definitions from templates ──────────────
        var forwardDef = await CreateAndActivateAsync(repository,
            "forward", "Webhook Forward",
            TriggerDefinition.CreateWebhook(Guid.NewGuid(), new WebhookTriggerConfiguration("forward")),
            [
                StepDefinition.CreateTransform(Guid.NewGuid(), "transform-input", 1,
                    new TransformStepConfiguration([
                        new TransformValueMapping("eventType", "{{input.type}}"),
                        new TransformValueMapping("payload", "{{input.data}}")
                    ])),
                StepDefinition.CreateHttpRequest(Guid.NewGuid(), "forward-payload", 2,
                    new HttpRequestStepConfiguration("https://httpbin.org/post", "POST"),
                    retryPolicy: new RetryPolicy(3, 15, BackoffStrategy.Fixed))
            ],
            "webhook-transform-forward",
            "Receives a webhook, normalizes the payload, and forwards it to a downstream HTTP endpoint. Retries automatically on failure.",
            ct);

        var chainDef = await CreateAndActivateAsync(repository,
            "api-chain", "API Chain",
            TriggerDefinition.CreateWebhook(Guid.NewGuid(), new WebhookTriggerConfiguration("api-chain")),
            [
                StepDefinition.CreateTransform(Guid.NewGuid(), "transform-for-api-a", 1,
                    new TransformStepConfiguration([
                        new TransformValueMapping("requestId", "{{input.id}}"),
                        new TransformValueMapping("action", "{{input.action}}")
                    ])),
                StepDefinition.CreateHttpRequest(Guid.NewGuid(), "call-api-a", 2,
                    new HttpRequestStepConfiguration("https://httpbin.org/post", "POST"),
                    retryPolicy: new RetryPolicy(3, 10, BackoffStrategy.Fixed)),
                StepDefinition.CreateTransform(Guid.NewGuid(), "transform-for-api-b", 3,
                    new TransformStepConfiguration([
                        new TransformValueMapping("sourceId", "{{steps.call-api-a.output.id}}"),
                        new TransformValueMapping("status", "{{steps.call-api-a.output.status}}")
                    ])),
                StepDefinition.CreateHttpRequest(Guid.NewGuid(), "call-api-b", 4,
                    new HttpRequestStepConfiguration("https://httpbin.org/post", "POST"),
                    retryPolicy: new RetryPolicy(3, 15, BackoffStrategy.Fixed))
            ],
            "webhook-api-chain",
            "Receives a webhook, transforms the payload, calls API A, transforms the result, then calls API B. Failure in later steps can be retried without re-running earlier completed steps.",
            ct);

        // Read back persisted records to get the DB-mapped IDs for step definitions
        var forwardRecord = await db.ExecutableWorkflowDefinitions
            .Include(d => d.StepDefinitions.OrderBy(s => s.Order))
            .FirstAsync(d => d.Key == "forward", ct);

        var chainRecord = await db.ExecutableWorkflowDefinitions
            .Include(d => d.StepDefinitions.OrderBy(s => s.Order))
            .FirstAsync(d => d.Key == "api-chain", ct);

        // ── Create instances for the forward workflow ────────────────────────────
        var now = DateTimeOffset.UtcNow;

        SeedCompletedInstance(db, forwardRecord, now.AddHours(-2), "forward-ok",
            """{"type":"order.created","data":{"orderId":1001}}""");

        SeedFailedInstance(db, forwardRecord, now.AddHours(-1), "forward-fail",
            """{"type":"user.deleted","data":{"userId":42}}""",
            "Connection refused: https://httpbin.org/post");

        SeedPendingInstance(db, forwardRecord, now.AddSeconds(-10), "forward-pending",
            """{"type":"invoice.sent","data":{"invoiceId":789}}""");

        // ── Create instances for the chain workflow ──────────────────────────────
        SeedCompletedInstance(db, chainRecord, now.AddHours(-3), "chain-ok",
            """{"id":"req-001","action":"sync","payload":{"customerId":"cust-42"}}""");

        SeedPartialFailureInstance(db, chainRecord, now.AddMinutes(-30), "chain-partial",
            """{"id":"req-002","action":"process","payload":{"customerId":"cust-99"}}""",
            "HTTP 502 Bad Gateway from https://httpbin.org/post");

        SeedRunningInstance(db, chainRecord, now.AddMinutes(-2), "chain-running",
            """{"id":"req-003","action":"validate","payload":{"customerId":"cust-55"}}""");

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Dev seed data created — 2 workflow definitions, 6 instances");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ── Definition creation ─────────────────────────────────────────────────────

    private static async Task<StepTrail.Shared.Definitions.WorkflowDefinition> CreateAndActivateAsync(
        IWorkflowDefinitionRepository repository,
        string key, string name,
        TriggerDefinition trigger,
        List<StepDefinition> steps,
        string sourceTemplateKey,
        string? description,
        CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        var definition = new StepTrail.Shared.Definitions.WorkflowDefinition(
            Guid.NewGuid(), key, name, 1,
            WorkflowDefinitionStatus.Inactive, trigger, steps, now, now,
            description,
            sourceTemplateKey: sourceTemplateKey, sourceTemplateVersion: 1);

        await repository.SaveNewVersionAsync(definition, ct);

        // Activate
        var saved = (await repository.GetActiveByKeyAsync(key, ct))
                    ?? await repository.GetByKeyAndVersionAsync(key, 1, ct);

        var activated = new StepTrail.Shared.Definitions.WorkflowDefinition(
            saved!.Id, saved.Key, saved.Name, saved.Version,
            WorkflowDefinitionStatus.Active, saved.TriggerDefinition, saved.StepDefinitions,
            saved.CreatedAtUtc, DateTimeOffset.UtcNow, saved.Description,
            saved.SourceTemplateKey, saved.SourceTemplateVersion);

        return await repository.UpdateAsync(activated, ct);
    }

    // ── Instance seeding ────────────────────────────────────────────────────────

    private static void SeedCompletedInstance(
        StepTrailDbContext db,
        ExecutableWorkflowDefinitionRecord def,
        DateTimeOffset start,
        string externalKey,
        string input)
    {
        var inst = CreateInstance(def, start, externalKey, input, WorkflowInstanceStatus.Completed,
            completedAt: start.AddMinutes(1));

        var elapsed = 0;
        foreach (var stepDef in def.StepDefinitions)
        {
            var stepStart = start.AddSeconds(elapsed + 5);
            var stepEnd = start.AddSeconds(elapsed + 12);
            db.WorkflowStepExecutions.Add(CreateStepExecution(inst, stepDef,
                WorkflowStepExecutionStatus.Completed, 1, stepStart, stepEnd,
                input: elapsed == 0 ? input : $"{{\"step\":\"{stepDef.Key}\",\"processed\":true}}",
                output: $"{{\"step\":\"{stepDef.Key}\",\"result\":\"ok\"}}"));
            db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.StepCompleted, stepEnd));
            elapsed += 15;
        }

        db.WorkflowInstances.Add(inst);
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowStarted, start));
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowCompleted, start.AddMinutes(1)));
    }

    private static void SeedFailedInstance(
        StepTrailDbContext db,
        ExecutableWorkflowDefinitionRecord def,
        DateTimeOffset start,
        string externalKey,
        string input,
        string error)
    {
        var inst = CreateInstance(def, start, externalKey, input, WorkflowInstanceStatus.Failed,
            completedAt: start.AddMinutes(2));

        var steps = def.StepDefinitions.ToList();

        // First step completes
        if (steps.Count > 0)
        {
            db.WorkflowStepExecutions.Add(CreateStepExecution(inst, steps[0],
                WorkflowStepExecutionStatus.Completed, 1,
                start.AddSeconds(5), start.AddSeconds(10),
                input: input, output: $"{{\"step\":\"{steps[0].Key}\",\"result\":\"ok\"}}"));
        }

        // Second step fails after 3 attempts
        if (steps.Count > 1)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var attemptStart = start.AddSeconds(15 + (attempt - 1) * 35);
                db.WorkflowStepExecutions.Add(CreateStepExecution(inst, steps[1],
                    WorkflowStepExecutionStatus.Failed, attempt,
                    attemptStart, attemptStart.AddSeconds(5),
                    input: $"{{\"step\":\"{steps[0].Key}\",\"result\":\"ok\"}}",
                    error: error));
            }
        }

        db.WorkflowInstances.Add(inst);
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowStarted, start));
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowFailed, start.AddMinutes(2)));
    }

    private static void SeedPendingInstance(
        StepTrailDbContext db,
        ExecutableWorkflowDefinitionRecord def,
        DateTimeOffset start,
        string externalKey,
        string input)
    {
        var inst = CreateInstance(def, start, externalKey, input, WorkflowInstanceStatus.Pending);

        var firstStep = def.StepDefinitions.OrderBy(s => s.Order).First();
        db.WorkflowStepExecutions.Add(new WorkflowStepExecution
        {
            Id = Guid.NewGuid(),
            WorkflowInstanceId = inst.Id,
            ExecutableStepDefinitionId = firstStep.Id,
            StepKey = firstStep.Key,
            StepOrder = firstStep.Order,
            StepType = firstStep.Type.ToString(),
            Status = WorkflowStepExecutionStatus.Pending,
            Attempt = 1,
            Input = input,
            ScheduledAt = start,
            CreatedAt = start,
            UpdatedAt = start
        });

        db.WorkflowInstances.Add(inst);
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowStarted, start));
    }

    private static void SeedRunningInstance(
        StepTrailDbContext db,
        ExecutableWorkflowDefinitionRecord def,
        DateTimeOffset start,
        string externalKey,
        string input)
    {
        var inst = CreateInstance(def, start, externalKey, input, WorkflowInstanceStatus.Running);
        var steps = def.StepDefinitions.OrderBy(s => s.Order).ToList();

        // First step completed
        if (steps.Count > 0)
        {
            db.WorkflowStepExecutions.Add(CreateStepExecution(inst, steps[0],
                WorkflowStepExecutionStatus.Completed, 1,
                start.AddSeconds(5), start.AddSeconds(10),
                input: input, output: $"{{\"step\":\"{steps[0].Key}\",\"result\":\"ok\"}}"));
        }

        // Second step running
        if (steps.Count > 1)
        {
            db.WorkflowStepExecutions.Add(new WorkflowStepExecution
            {
                Id = Guid.NewGuid(),
                WorkflowInstanceId = inst.Id,
                ExecutableStepDefinitionId = steps[1].Id,
                StepKey = steps[1].Key,
                StepOrder = steps[1].Order,
                StepType = steps[1].Type.ToString(),
                Status = WorkflowStepExecutionStatus.Running,
                Attempt = 1,
                Input = $"{{\"step\":\"{steps[0].Key}\",\"result\":\"ok\"}}",
                ScheduledAt = start.AddSeconds(10),
                StartedAt = start.AddSeconds(15),
                LockedBy = "worker-dev-001",
                LockedAt = start.AddSeconds(15),
                CreatedAt = start.AddSeconds(10),
                UpdatedAt = start.AddSeconds(15)
            });
        }

        db.WorkflowInstances.Add(inst);
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowStarted, start));
    }

    private static void SeedPartialFailureInstance(
        StepTrailDbContext db,
        ExecutableWorkflowDefinitionRecord def,
        DateTimeOffset start,
        string externalKey,
        string input,
        string error)
    {
        // Chain workflow: steps 1,2 completed, step 3 completed, step 4 failed
        var inst = CreateInstance(def, start, externalKey, input, WorkflowInstanceStatus.Failed,
            completedAt: start.AddMinutes(3));
        var steps = def.StepDefinitions.OrderBy(s => s.Order).ToList();

        var elapsed = 0;
        for (var i = 0; i < steps.Count; i++)
        {
            var stepStart = start.AddSeconds(elapsed + 5);
            var stepEnd = start.AddSeconds(elapsed + 12);
            var isLast = i == steps.Count - 1;

            db.WorkflowStepExecutions.Add(CreateStepExecution(inst, steps[i],
                isLast ? WorkflowStepExecutionStatus.Failed : WorkflowStepExecutionStatus.Completed,
                1, stepStart, stepEnd,
                input: i == 0 ? input : $"{{\"step\":\"{steps[i - 1].Key}\",\"result\":\"ok\"}}",
                output: isLast ? null : $"{{\"step\":\"{steps[i].Key}\",\"result\":\"ok\"}}",
                error: isLast ? error : null));
            elapsed += 15;
        }

        db.WorkflowInstances.Add(inst);
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowStarted, start));
        db.WorkflowEvents.Add(Event(inst, WorkflowEventTypes.WorkflowFailed, start.AddMinutes(3)));
    }

    // ── Factories ───────────────────────────────────────────────────────────────

    private static WorkflowInstance CreateInstance(
        ExecutableWorkflowDefinitionRecord def,
        DateTimeOffset createdAt,
        string externalKey,
        string input,
        WorkflowInstanceStatus status,
        DateTimeOffset? completedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        ExecutableWorkflowDefinitionId = def.Id,
        WorkflowDefinitionKey = def.Key,
        WorkflowDefinitionVersion = def.Version,
        ExternalKey = externalKey,
        Status = status,
        Input = input,
        CreatedAt = createdAt,
        UpdatedAt = completedAt ?? createdAt,
        CompletedAt = completedAt
    };

    private static WorkflowStepExecution CreateStepExecution(
        WorkflowInstance inst,
        ExecutableStepDefinitionRecord stepDef,
        WorkflowStepExecutionStatus status,
        int attempt,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string? input = null,
        string? output = null,
        string? error = null) => new()
    {
        Id = Guid.NewGuid(),
        WorkflowInstanceId = inst.Id,
        ExecutableStepDefinitionId = stepDef.Id,
        StepKey = stepDef.Key,
        StepOrder = stepDef.Order,
        StepType = stepDef.Type.ToString(),
        Status = status,
        Attempt = attempt,
        Input = input,
        Output = output,
        Error = error,
        ScheduledAt = startedAt.AddSeconds(-5),
        StartedAt = startedAt,
        CompletedAt = completedAt,
        CreatedAt = startedAt.AddSeconds(-5),
        UpdatedAt = completedAt
    };

    private static WorkflowEvent Event(
        WorkflowInstance inst, string eventType, DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        WorkflowInstanceId = inst.Id,
        EventType = eventType,
        CreatedAt = createdAt
    };
}
