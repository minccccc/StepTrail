using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Services;

/// <summary>
/// Seeds representative workflow instances for local development only.
/// Creates one instance per status so every UI state is visible immediately.
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

        // Idempotency check — bail out if already seeded
        if (await db.WorkflowInstances.AnyAsync(i => i.ExternalKey == "seed-alice", ct))
        {
            _logger.LogDebug("Dev seed data already present — skipping");
            return;
        }

        // Load the workflow definition (synced by WorkflowDefinitionSyncService before us)
        var definition = await db.WorkflowDefinitions
            .Include(d => d.Steps)
            .FirstOrDefaultAsync(d => d.Key == "user-onboarding" && d.Version == 1, ct);

        if (definition is null)
        {
            _logger.LogWarning("Workflow definition 'user-onboarding' v1 not found — dev seed skipped");
            return;
        }

        var step1 = definition.Steps.First(s => s.Order == 1); // send-welcome-email
        var step2 = definition.Steps.First(s => s.Order == 2); // provision-account
        var step3 = definition.Steps.First(s => s.Order == 3); // notify-team

        var now = DateTimeOffset.UtcNow;

        // ── 1. Completed ────────────────────────────────────────────────────────────
        SeedCompleted(db, definition, step1, step2, step3, now);

        // ── 2. Failed — exhausted all retries on step 2 ─────────────────────────────
        SeedFailedMaxRetries(db, definition, step1, step2, now);

        // ── 3. Failed — first attempt failure, retryable ────────────────────────────
        SeedFailedRetryable(db, definition, step1, now);

        // ── 4. Running — step 2 currently executing ─────────────────────────────────
        SeedRunning(db, definition, step1, step2, now);

        // ── 5. Pending — just created, nothing started yet ──────────────────────────
        SeedPending(db, definition, step1, now);

        // ── 6. Cancelled ────────────────────────────────────────────────────────────
        SeedCancelled(db, definition, step1, step2, now);

        await db.SaveChangesAsync(ct);
        _logger.LogInformation("Dev seed data created — 6 workflow instances across all statuses");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ────────────────────────────────────────────────────────────────────────────────

    private static void SeedCompleted(
        StepTrailDbContext db,
        WorkflowDefinition def,
        WorkflowDefinitionStep s1,
        WorkflowDefinitionStep s2,
        WorkflowDefinitionStep s3,
        DateTimeOffset now)
    {
        var start = now.AddHours(-2);
        var inst = Instance(def, "seed-alice", WorkflowInstanceStatus.Completed,
            input: """{"userId":"alice@example.com","plan":"pro"}""",
            createdAt: start, completedAt: start.AddMinutes(3));

        var e1 = StepExec(inst, s1, WorkflowStepExecutionStatus.Completed, attempt: 1,
            scheduledAt: start,
            startedAt: start.AddSeconds(5),
            completedAt: start.AddSeconds(12),
            input: """{"userId":"alice@example.com","plan":"pro"}""",
            output: """{"emailSent":true,"recipient":"alice@example.com"}""");

        var e2 = StepExec(inst, s2, WorkflowStepExecutionStatus.Completed, attempt: 1,
            scheduledAt: start.AddSeconds(12),
            startedAt: start.AddSeconds(17),
            completedAt: start.AddSeconds(45),
            input: """{"emailSent":true,"recipient":"alice@example.com"}""",
            output: """{"accountId":"acc-10001","status":"active"}""");

        var e3 = StepExec(inst, s3, WorkflowStepExecutionStatus.Completed, attempt: 1,
            scheduledAt: start.AddSeconds(45),
            startedAt: start.AddSeconds(50),
            completedAt: start.AddMinutes(3),
            input: """{"accountId":"acc-10001","status":"active"}""",
            output: """{"notified":true,"channel":"#new-users"}""");

        db.WorkflowInstances.Add(inst);
        db.WorkflowStepExecutions.AddRange(e1, e2, e3);
        db.WorkflowEvents.AddRange(
            Event(inst, null,  WorkflowEventTypes.WorkflowStarted,   start),
            Event(inst, e1,    WorkflowEventTypes.StepStarted,        start.AddSeconds(5)),
            Event(inst, e1,    WorkflowEventTypes.StepCompleted,      start.AddSeconds(12)),
            Event(inst, e2,    WorkflowEventTypes.StepStarted,        start.AddSeconds(17)),
            Event(inst, e2,    WorkflowEventTypes.StepCompleted,      start.AddSeconds(45)),
            Event(inst, e3,    WorkflowEventTypes.StepStarted,        start.AddSeconds(50)),
            Event(inst, e3,    WorkflowEventTypes.StepCompleted,      start.AddMinutes(3)),
            Event(inst, null,  WorkflowEventTypes.WorkflowCompleted,  start.AddMinutes(3)));
    }

    private static void SeedFailedMaxRetries(
        StepTrailDbContext db,
        WorkflowDefinition def,
        WorkflowDefinitionStep s1,
        WorkflowDefinitionStep s2,
        DateTimeOffset now)
    {
        var start = now.AddHours(-1);
        const string provisionError = "AccountService: quota exceeded — no capacity for new accounts";

        var inst = Instance(def, "seed-bob", WorkflowInstanceStatus.Failed,
            input: """{"userId":"bob@example.com","plan":"enterprise"}""",
            createdAt: start, completedAt: start.AddMinutes(3));

        var e1 = StepExec(inst, s1, WorkflowStepExecutionStatus.Completed, attempt: 1,
            scheduledAt: start, startedAt: start.AddSeconds(5), completedAt: start.AddSeconds(10),
            input: """{"userId":"bob@example.com","plan":"enterprise"}""",
            output: """{"emailSent":true,"recipient":"bob@example.com"}""");

        var f1 = StepExec(inst, s2, WorkflowStepExecutionStatus.Failed, attempt: 1,
            scheduledAt: start.AddSeconds(10), startedAt: start.AddSeconds(15),
            completedAt: start.AddSeconds(18),
            input: """{"emailSent":true,"recipient":"bob@example.com"}""",
            error: provisionError);

        var f2 = StepExec(inst, s2, WorkflowStepExecutionStatus.Failed, attempt: 2,
            scheduledAt: start.AddSeconds(48), startedAt: start.AddSeconds(53),
            completedAt: start.AddSeconds(57),
            input: """{"emailSent":true,"recipient":"bob@example.com"}""",
            error: provisionError);

        var f3 = StepExec(inst, s2, WorkflowStepExecutionStatus.Failed, attempt: 3,
            scheduledAt: start.AddSeconds(87), startedAt: start.AddSeconds(92),
            completedAt: start.AddSeconds(96),
            input: """{"emailSent":true,"recipient":"bob@example.com"}""",
            error: provisionError);

        db.WorkflowInstances.Add(inst);
        db.WorkflowStepExecutions.AddRange(e1, f1, f2, f3);
        db.WorkflowEvents.AddRange(
            Event(inst, null, WorkflowEventTypes.WorkflowStarted,    start),
            Event(inst, e1,   WorkflowEventTypes.StepStarted,         start.AddSeconds(5)),
            Event(inst, e1,   WorkflowEventTypes.StepCompleted,       start.AddSeconds(10)),
            Event(inst, f1,   WorkflowEventTypes.StepStarted,         start.AddSeconds(15)),
            Event(inst, f1,   WorkflowEventTypes.StepFailed,          start.AddSeconds(18)),
            Event(inst, f2,   WorkflowEventTypes.StepRetryScheduled,  start.AddSeconds(18)),
            Event(inst, f2,   WorkflowEventTypes.StepStarted,         start.AddSeconds(53)),
            Event(inst, f2,   WorkflowEventTypes.StepFailed,          start.AddSeconds(57)),
            Event(inst, f3,   WorkflowEventTypes.StepRetryScheduled,  start.AddSeconds(57)),
            Event(inst, f3,   WorkflowEventTypes.StepStarted,         start.AddSeconds(92)),
            Event(inst, f3,   WorkflowEventTypes.StepFailed,          start.AddSeconds(96)),
            Event(inst, null, WorkflowEventTypes.WorkflowFailed,      start.AddSeconds(96)));
    }

    private static void SeedFailedRetryable(
        StepTrailDbContext db,
        WorkflowDefinition def,
        WorkflowDefinitionStep s1,
        DateTimeOffset now)
    {
        var start = now.AddMinutes(-20);

        var inst = Instance(def, "seed-charlie", WorkflowInstanceStatus.Failed,
            input: """{"userId":"charlie@example.com","plan":"starter"}""",
            createdAt: start, completedAt: start.AddMinutes(1));

        var f1 = StepExec(inst, s1, WorkflowStepExecutionStatus.Failed, attempt: 1,
            scheduledAt: start, startedAt: start.AddSeconds(5), completedAt: start.AddSeconds(8),
            input: """{"userId":"charlie@example.com","plan":"starter"}""",
            error: "SMTP connection refused at smtp.example.com:587");

        db.WorkflowInstances.Add(inst);
        db.WorkflowStepExecutions.Add(f1);
        db.WorkflowEvents.AddRange(
            Event(inst, null, WorkflowEventTypes.WorkflowStarted, start),
            Event(inst, f1,   WorkflowEventTypes.StepStarted,      start.AddSeconds(5)),
            Event(inst, f1,   WorkflowEventTypes.StepFailed,       start.AddSeconds(8)),
            Event(inst, null, WorkflowEventTypes.WorkflowFailed,   start.AddSeconds(8)));
    }

    private static void SeedRunning(
        StepTrailDbContext db,
        WorkflowDefinition def,
        WorkflowDefinitionStep s1,
        WorkflowDefinitionStep s2,
        DateTimeOffset now)
    {
        var start = now.AddMinutes(-2);

        var inst = Instance(def, "seed-dave", WorkflowInstanceStatus.Running,
            input: """{"userId":"dave@example.com","plan":"pro"}""",
            createdAt: start);

        var e1 = StepExec(inst, s1, WorkflowStepExecutionStatus.Completed, attempt: 1,
            scheduledAt: start, startedAt: start.AddSeconds(5), completedAt: start.AddSeconds(11),
            input: """{"userId":"dave@example.com","plan":"pro"}""",
            output: """{"emailSent":true,"recipient":"dave@example.com"}""");

        var r2 = StepExec(inst, s2, WorkflowStepExecutionStatus.Running, attempt: 1,
            scheduledAt: start.AddSeconds(11), startedAt: start.AddSeconds(16),
            input: """{"emailSent":true,"recipient":"dave@example.com"}""",
            lockedBy: "worker-dev-001");

        db.WorkflowInstances.Add(inst);
        db.WorkflowStepExecutions.AddRange(e1, r2);
        db.WorkflowEvents.AddRange(
            Event(inst, null, WorkflowEventTypes.WorkflowStarted, start),
            Event(inst, e1,   WorkflowEventTypes.StepStarted,      start.AddSeconds(5)),
            Event(inst, e1,   WorkflowEventTypes.StepCompleted,    start.AddSeconds(11)),
            Event(inst, r2,   WorkflowEventTypes.StepStarted,      start.AddSeconds(16)));
    }

    private static void SeedPending(
        StepTrailDbContext db,
        WorkflowDefinition def,
        WorkflowDefinitionStep s1,
        DateTimeOffset now)
    {
        var start = now.AddSeconds(-10);

        var inst = Instance(def, "seed-eve", WorkflowInstanceStatus.Pending,
            input: """{"userId":"eve@example.com","plan":"starter"}""",
            createdAt: start);

        var p1 = StepExec(inst, s1, WorkflowStepExecutionStatus.Pending, attempt: 1,
            scheduledAt: start,
            input: """{"userId":"eve@example.com","plan":"starter"}""");

        db.WorkflowInstances.Add(inst);
        db.WorkflowStepExecutions.Add(p1);
        db.WorkflowEvents.Add(Event(inst, null, WorkflowEventTypes.WorkflowStarted, start));
    }

    private static void SeedCancelled(
        StepTrailDbContext db,
        WorkflowDefinition def,
        WorkflowDefinitionStep s1,
        WorkflowDefinitionStep s2,
        DateTimeOffset now)
    {
        var start = now.AddHours(-3);
        var cancelled = start.AddMinutes(30);

        var inst = Instance(def, "seed-frank", WorkflowInstanceStatus.Cancelled,
            input: """{"userId":"frank@example.com","plan":"pro"}""",
            createdAt: start, completedAt: cancelled);

        var e1 = StepExec(inst, s1, WorkflowStepExecutionStatus.Completed, attempt: 1,
            scheduledAt: start, startedAt: start.AddSeconds(5), completedAt: start.AddSeconds(10),
            input: """{"userId":"frank@example.com","plan":"pro"}""",
            output: """{"emailSent":true,"recipient":"frank@example.com"}""");

        var c2 = StepExec(inst, s2, WorkflowStepExecutionStatus.Cancelled, attempt: 1,
            scheduledAt: start.AddSeconds(10),
            input: """{"emailSent":true,"recipient":"frank@example.com"}""");

        db.WorkflowInstances.Add(inst);
        db.WorkflowStepExecutions.AddRange(e1, c2);
        db.WorkflowEvents.AddRange(
            Event(inst, null, WorkflowEventTypes.WorkflowStarted,   start),
            Event(inst, e1,   WorkflowEventTypes.StepStarted,        start.AddSeconds(5)),
            Event(inst, e1,   WorkflowEventTypes.StepCompleted,      start.AddSeconds(10)),
            Event(inst, null, WorkflowEventTypes.WorkflowCancelled,  cancelled));
    }

    // ── Factories ────────────────────────────────────────────────────────────────────

    private static WorkflowInstance Instance(
        WorkflowDefinition def,
        string externalKey,
        WorkflowInstanceStatus status,
        string? input,
        DateTimeOffset createdAt,
        DateTimeOffset? completedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        TenantId = TenantId,
        WorkflowDefinitionId = def.Id,
        ExternalKey = externalKey,
        Status = status,
        Input = input,
        CreatedAt = createdAt,
        UpdatedAt = completedAt ?? createdAt,
        CompletedAt = completedAt
    };

    private static WorkflowStepExecution StepExec(
        WorkflowInstance inst,
        WorkflowDefinitionStep stepDef,
        WorkflowStepExecutionStatus status,
        int attempt,
        DateTimeOffset scheduledAt,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        string? input = null,
        string? output = null,
        string? error = null,
        string? lockedBy = null) => new()
    {
        Id = Guid.NewGuid(),
        WorkflowInstanceId = inst.Id,
        WorkflowDefinitionStepId = stepDef.Id,
        StepKey = stepDef.StepKey,
        Status = status,
        Attempt = attempt,
        Input = input,
        Output = output,
        Error = error,
        ScheduledAt = scheduledAt,
        LockedAt = lockedBy is not null ? startedAt : null,
        LockedBy = lockedBy,
        StartedAt = startedAt,
        CompletedAt = completedAt,
        CreatedAt = scheduledAt,
        UpdatedAt = completedAt ?? startedAt ?? scheduledAt
    };

    private static WorkflowEvent Event(
        WorkflowInstance inst,
        WorkflowStepExecution? stepExec,
        string eventType,
        DateTimeOffset createdAt) => new()
    {
        Id = Guid.NewGuid(),
        WorkflowInstanceId = inst.Id,
        StepExecutionId = stepExec?.Id,
        EventType = eventType,
        CreatedAt = createdAt
    };
}
