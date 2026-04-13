# StepTrail — Architecture

## Overview

StepTrail is a workflow orchestration engine. It allows you to define multi-step background processes as code-first workflow descriptors, start instances of those workflows via REST API or webhook, and execute each step reliably through a background worker service — with automatic retries, configurable timeouts, orphan detection, recurring schedules, outbound HTTP activity steps, secrets management, operational alerting, and a browser-based operations console.

The system is intentionally simple: **PostgreSQL is the coordination layer**. There is no message queue, no in-process scheduler, no distributed cache. All state lives in the database, and workers coordinate via database-level row locking.

---

## System Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│             External Callers / Browser                           │
│         (webhook senders, ops console users)                     │
└────────────────────────┬────────────────────────────────────────┘
                         │ HTTP
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                     StepTrail.Api                                │
│                                                                  │
│  Startup services:                                               │
│    TenantSeedService              (seeds dev tenant)             │
│    WorkflowDefinitionSyncService  (code → DB sync, schedules)    │
│    DevDataSeedService             (dev-only sample data)         │
│                                                                  │
│  Request services:                                               │
│    WorkflowInstanceService   (start, idempotency)                │
│    WorkflowQueryService      (list, detail, timeline)            │
│    WorkflowRetryService      (retry, replay, archive, cancel)    │
│                                                                  │
│  HTTP surface:                                                   │
│    Public: GET /health, POST /webhooks/{key}                     │
│    Ops API (auth required): /workflows, /workflow-instances/*,   │
│                             /secrets                             │
│    Ops UI (auth required):  /ops/** Razor Pages                  │
│    Auth:  GET /login, POST /logout                               │
│    Docs:  /openapi/v1.json, /scalar/v1                           │
└────────────────────────┬────────────────────────────────────────┘
                         │ EF Core / Npgsql
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                      PostgreSQL                                  │
│                                                                  │
│  tenants                    workflow_definitions                  │
│  users                      workflow_definition_steps            │
│  workflow_instances         workflow_step_executions             │
│  workflow_events            idempotency_records                  │
│  recurring_workflow_schedules                                    │
│  workflow_secrets                                                │
└────────────────────────┬────────────────────────────────────────┘
                         │ EF Core / Npgsql
                         ▼
┌─────────────────────────────────────────────────────────────────┐
│                    StepTrail.Worker                              │
│                                                                  │
│  Worker (BackgroundService) per-loop:                            │
│    RecurringWorkflowDispatcher  (SELECT FOR UPDATE SKIP LOCKED)  │
│    StuckExecutionDetector       (orphan recovery)                │
│    StepExecutionClaimer         (SELECT FOR UPDATE SKIP LOCKED)  │
│    StepExecutionProcessor       (resolve handler, run, persist)  │
│    StepLeaseRenewer             (heartbeat during execution)     │
│                                                                  │
│  Step Handlers (keyed DI):                                       │
│    SendWelcomeEmailHandler                                       │
│    ProvisionAccountHandler                                       │
│    NotifyTeamHandler                                             │
│    HttpActivityHandler          (outbound HTTP calls)            │
│                                                                  │
│  Support services:                                               │
│    SecretResolver               ({{secrets.name}} substitution)  │
│    AlertService / IAlertChannel (failure notifications)          │
└─────────────────────────────────────────────────────────────────┘
```

---

## Projects

### StepTrail.Shared

A class library referenced by both the API and the Worker. Contains everything that needs to be shared:

- **Domain entities** — 10 EF Core entity classes mapping to DB tables
- **Entity configurations** — EF Core Fluent API config (column names, constraints, indexes)
- **DbContext** — `StepTrailDbContext` with all 10 `DbSet<T>` properties
- **Workflow abstractions** — `WorkflowDescriptor`, `WorkflowStepDescriptor`, `IWorkflowRegistry`, `IStepHandler`, `StepContext`, `StepResult`
- **DI helpers** — extension methods `AddWorkflow<T>()`, `AddWorkflowRegistry()`, `AddStepTrailDb()`

### StepTrail.Api

An ASP.NET Core application (Minimal API + Razor Pages). Responsibilities:

- **Schema ownership** — EF Core migrations, applied on startup via `db.Database.MigrateAsync()`
- **Workflow registry** — syncs code-first workflow definitions and recurring schedules to the DB at startup
- **Instance lifecycle** — starting instances (via REST or webhook), idempotency, manual retry/replay/archive/cancel
- **Secrets management** — CRUD for named workflow secrets (values never returned by API)
- **Ops console** — cookie-authenticated Razor Pages UI at `/ops/**`
- **API documentation** — OpenAPI spec + Scalar UI

The API does not execute steps. It only creates and queries state.

### StepTrail.Worker

A .NET Worker Service (`BackgroundService`). Each loop iteration:

1. **Dispatches recurring schedules** — `RecurringWorkflowDispatcher` creates new instances for any enabled schedules whose `next_run_at` is due
2. **Recovers orphaned steps** — `StuckExecutionDetector` requeues `Running` executions whose lease has expired (worker crash recovery)
3. **Claims and executes one step** — `StepExecutionClaimer` atomically claims a `Pending` execution; `StepExecutionProcessor` runs it with lease renewal and optional per-step timeout
4. **Fires alerts** — `AlertService` notifies configured channels on permanent failure or orphaned steps

The Worker has no REST endpoints and does not own the database schema.

---

## Key Design Decisions

### 1. PostgreSQL as the Coordination Layer

There is no message queue. Workers compete to claim rows using `SELECT ... FOR UPDATE SKIP LOCKED`, which is a battle-tested pattern for work queues directly in PostgreSQL. Recurring schedule dispatch, orphan detection, and step claiming all use this same pattern.

### 2. Code-First Workflow Definitions

Workflows extend `WorkflowDescriptor`. At startup, `WorkflowDefinitionSyncService` idempotently writes them to the `workflow_definitions` and `workflow_definition_steps` tables, including step config JSON and timeout settings.

```csharp
public sealed class WebhookToHttpCallWorkflow : WorkflowDescriptor
{
    public override string Key     => "webhook-to-http-call";
    public override int    Version => 1;
    public override string Name    => "Webhook → HTTP Call";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new WorkflowStepDescriptor(
            stepKey:           "http-call",
            stepType:          "HttpActivityHandler",
            order:             1,
            maxAttempts:       3,
            retryDelaySeconds: 30,
            timeoutSeconds:    30,
            config: new { Url = "{{secrets.webhook-to-http-call-url}}", Method = "POST" })
    ];
}
```

Bumping `Version` creates a new definition row; old versions continue running existing instances.

### 3. Per-Step Timeout Enforcement

Each step definition carries an optional `TimeoutSeconds`. The processor creates a `CancellationTokenSource.CancelAfter(...)` linked to the worker shutdown token and passes it to the handler. A `StepLeaseRenewer` heartbeats `lock_expires_at` in the background to prevent the `StuckExecutionDetector` from treating a legitimately slow step as an orphan. Timeout cancellation is cooperative — any handler that awaits the CT will be cancelled; a truly blocking handler cannot be forcibly terminated.

### 4. Orphan Recovery

If a worker process crashes mid-execution, the lease heartbeat stops. `StuckExecutionDetector` scans for `Running` rows with `lock_expires_at <= now` and re-queues them via the normal failure/retry path (event type `StepOrphaned`).

### 5. Secrets and Placeholder Resolution

Named secrets are stored encrypted at rest in `workflow_secrets`. Step configurations can reference them as `{{secrets.name}}`. `SecretResolver` in the worker batch-loads referenced secrets from the DB and substitutes them before executing the step — secrets are never persisted in step config or execution rows.

### 6. Recurring Workflows

A `WorkflowDescriptor` can declare `RecurrenceIntervalSeconds`. `WorkflowDefinitionSyncService` creates a `recurring_workflow_schedules` row on first sync. `RecurringWorkflowDispatcher` (worker) polls that table, fires new instances for due schedules, and advances `next_run_at`.

### 7. Webhook Trigger

`POST /webhooks/{workflowKey}` is an unauthenticated public endpoint that starts a workflow instance. The full JSON request body becomes the first step's input. Idempotency and external correlation keys are supplied as HTTP headers.

### 8. Operational Alerts

`AlertService` fans out to all registered `IAlertChannel` implementations after `WorkflowFailed` or `StepOrphaned` events. `ConsoleLogAlertChannel` is always active. `WebhookAlertChannel` is registered when `Alerts:WebhookUrl` is configured. Channel failures are logged and never propagate.

### 9. Ops Console Authentication

All Razor Pages under `/ops/**` and all ops API endpoints require a valid session cookie. `ForwardAuthCookieHandler` (a `DelegatingHandler` on `WorkflowApiClient`) copies the browser cookie onto loopback API calls so the same-process REST layer is also authenticated. Credentials are stored in `Ops:Username` / `Ops:Password` config, overridable via environment variables.

### 10. Idempotency

`POST /workflow-instances` accepts an optional `IdempotencyKey`. Duplicate requests return the original instance. Implemented via a `UNIQUE` constraint on `(tenant_id, idempotency_key)` backed by a pre-check + catch on `PostgresException { SqlState: "23505" }`.

### 11. Step-to-Step Data Flow

Step handlers receive a `StepContext` containing the previous step's output as `Input` (a JSON string). On success, `Output` becomes the `Input` of the next step. On `HttpActivityHandler` failure, the HTTP response status + body are persisted as `Output` even on the failed execution row — useful for diagnosing what the remote server returned.

---

## Execution Lifecycle

```
Client / Webhook                API                      DB                       Worker
  │                              │                        │                          │
  │  POST /webhooks/wf-key ────► │                        │                          │
  │   (or POST /workflow-        │  validate              │                          │
  │    instances)                │  check idempotency ──► │                          │
  │                              │  insert instance       │                          │
  │                              │  insert step exec ──── ▼                          │
  │                              │  insert event    (Pending, Attempt 1)             │
  │  201 Created ◄────────────── │                        │                          │
  │                              │                        │  per-loop:               │
  │                              │                        │  1. dispatch recurring   │
  │                              │                        │  2. recover orphans      │
  │                              │                        │  3. SELECT FOR UPDATE    │
  │                              │                        │     SKIP LOCKED ───────► │
  │                              │                        │  step → Running          │
  │                              │                        │  StepStarted event       │
  │                              │                        │                          │
  │                              │                        │  lease renewer heartbeat │
  │                              │                        │  handler.ExecuteAsync()  │
  │                              │                        │  [success]               │
  │                              │                        │  step → Completed ◄───── │
  │                              │                        │  Output stored           │
  │                              │                        │  StepCompleted event     │
  │                              │                        │  next step inserted      │
  │                              │                        │  (or instance Completed) │
```

### Failure / Timeout Path

```
  [handler throws, times out, or orphan detected]
  step → Failed, error + output stored
  StepFailed / StepTimedOut / StepOrphaned event
  if attempt < MaxAttempts:
    new execution (Attempt+1, ScheduledAt = now + RetryDelaySeconds)
    StepRetryScheduled event
  else:
    instance → Failed
    WorkflowFailed event
    AlertService.SendAsync("WorkflowFailed", ...)
```

---

## Database Schema

### Tables

#### `tenants`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `name` | `varchar(200)` | |
| `created_at` | `timestamptz` | |

#### `users`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK → tenants |
| `username` | `varchar(200)` | |
| `email` | `varchar(300)` | UNIQUE per tenant |
| `created_at` | `timestamptz` | |

#### `workflow_definitions`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `key` | `varchar(200)` | UNIQUE with version |
| `version` | `int` | UNIQUE with key |
| `name` | `varchar(200)` | |
| `description` | `varchar(1000)` | nullable |
| `created_at` | `timestamptz` | |

#### `workflow_definition_steps`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `workflow_definition_id` | `uuid` | FK → workflow_definitions |
| `step_key` | `varchar(200)` | UNIQUE per definition |
| `step_type` | `varchar(500)` | handler class name (keyed DI key) |
| `order` | `int` | UNIQUE per definition, 1-based |
| `max_attempts` | `int` | default 3 |
| `retry_delay_seconds` | `int` | default 30 |
| `timeout_seconds` | `int` | nullable — per-step execution timeout |
| `config` | `jsonb` | nullable — handler-specific configuration |
| `created_at` | `timestamptz` | |

#### `workflow_instances`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK → tenants |
| `workflow_definition_id` | `uuid` | FK → workflow_definitions |
| `external_key` | `varchar(500)` | nullable, caller-provided business key |
| `idempotency_key` | `varchar(500)` | nullable, deduplication key |
| `status` | `varchar(50)` | Pending / Running / Completed / Failed / Cancelled / Archived |
| `input` | `jsonb` | nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |
| `completed_at` | `timestamptz` | nullable |

#### `workflow_step_executions`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `workflow_instance_id` | `uuid` | FK → workflow_instances |
| `workflow_definition_step_id` | `uuid` | FK → workflow_definition_steps |
| `step_key` | `varchar(200)` | denormalised for fast queries |
| `status` | `varchar(50)` | Pending / Running / Completed / Failed / Cancelled |
| `attempt` | `int` | 1-based, increments per retry |
| `input` | `jsonb` | nullable |
| `output` | `jsonb` | nullable — set on success **and** on `HttpActivityHandler` failure |
| `error` | `text` | nullable, set on failure |
| `scheduled_at` | `timestamptz` | when worker may pick it up |
| `locked_at` | `timestamptz` | nullable, when worker claimed it |
| `locked_by` | `varchar(200)` | nullable, worker ID |
| `lock_expires_at` | `timestamptz` | nullable, renewed by StepLeaseRenewer |
| `started_at` | `timestamptz` | nullable, when handler began |
| `completed_at` | `timestamptz` | nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Critical index:** `(status, scheduled_at)` — used by the worker's claim query on every poll.
**Lock index:** `(status, lock_expires_at)` — used by `StuckExecutionDetector`.

#### `workflow_events`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `workflow_instance_id` | `uuid` | FK → workflow_instances |
| `step_execution_id` | `uuid` | nullable FK → workflow_step_executions |
| `event_type` | `varchar(100)` | see event types below |
| `payload` | `jsonb` | nullable |
| `created_at` | `timestamptz` | |

#### `idempotency_records`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK → tenants |
| `idempotency_key` | `varchar(500)` | UNIQUE with tenant_id |
| `workflow_instance_id` | `uuid` | FK → workflow_instances |
| `created_at` | `timestamptz` | |

#### `recurring_workflow_schedules`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `workflow_definition_id` | `uuid` | UNIQUE FK → workflow_definitions |
| `tenant_id` | `uuid` | FK → tenants |
| `interval_seconds` | `int` | repeat interval |
| `is_enabled` | `bool` | disabling pauses dispatch without deletion |
| `input` | `jsonb` | nullable, forwarded to each new instance |
| `last_run_at` | `timestamptz` | nullable |
| `next_run_at` | `timestamptz` | when dispatcher next fires this schedule |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Index:** `(is_enabled, next_run_at)` — used by `RecurringWorkflowDispatcher`.

#### `workflow_secrets`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `name` | `varchar(200)` | UNIQUE — used in `{{secrets.name}}` placeholders |
| `value` | `text` | secret value — never returned by any API endpoint |
| `description` | `varchar(500)` | nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

### Event Types

| Event | Level | Description |
|-------|-------|-------------|
| `WorkflowStarted` | instance | new instance created |
| `StepStarted` | step | handler began executing |
| `StepCompleted` | step | handler succeeded |
| `StepFailed` | step | handler threw an exception |
| `StepTimedOut` | step | handler exceeded `TimeoutSeconds` |
| `StepOrphaned` | step | lease expired without completion (worker crash) |
| `StepRetryScheduled` | step | new attempt queued after failure |
| `WorkflowCompleted` | instance | all steps completed |
| `WorkflowFailed` | instance | step exhausted all attempts |
| `WorkflowRetried` | instance | manual retry from last failed step |
| `WorkflowReplayed` | instance | manual replay from step 1 |
| `WorkflowCancelled` | instance | manually cancelled |
| `WorkflowArchived` | instance | manually archived |

---

## Migrations

Migrations are EF Core code-first migrations in `src/StepTrail.Api/Migrations/`. The API owns the schema; the Worker reads and writes but never migrates.

| Migration | Change |
|-----------|--------|
| `20260410152221_InitialSchema` | All base tables, constraints, indexes |
| `20260410190826_RemoveTenantFromWorkflowDefinition` | Workflow definitions made global |
| `20260410192716_AddRetryPolicyToWorkflowDefinitionStep` | `max_attempts`, `retry_delay_seconds` |
| `20260412125029_AddTimeoutAndLockExpiry` | `timeout_seconds` on steps; `lock_expires_at`, `started_at` on executions |
| `20260412131217_AddRecurringWorkflowSchedules` | `recurring_workflow_schedules` table |
| `20260412132732_AddStepConfig` | `config` (jsonb) on `workflow_definition_steps` |
| `20260412133604_AddWorkflowSecrets` | `workflow_secrets` table |

Migrations are applied automatically at API startup.

To add a new migration (run from repo root):

```bash
dotnet ef migrations add YourMigrationName \
  --project src/StepTrail.Api \
  --startup-project src/StepTrail.Api
```

---

## Configuration

### API (`src/StepTrail.Api/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "StepTrailDb": "Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail"
  },
  "UI": {
    "ApiBaseUrl": "http://localhost:5000"
  },
  "Ops": {
    "Username": "admin",
    "Password": "admin"
  }
}
```

`Ops:Username` and `Ops:Password` should be overridden via environment variables in any non-local deployment (`Ops__Username`, `Ops__Password`).

### Worker (`src/StepTrail.Worker/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "StepTrailDb": "Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail"
  },
  "Worker": {
    "PollIntervalSeconds": 5,
    "HeartbeatIntervalSeconds": 60,
    "DefaultLockExpirySeconds": 300
  },
  "Alerts": {
    "WebhookUrl": ""
  }
}
```

Set `Alerts:WebhookUrl` to a non-empty URL to enable webhook-based failure notifications.

---

## Scaling

**Scale the Worker horizontally** — run multiple instances pointing at the same database. `SELECT ... FOR UPDATE SKIP LOCKED` ensures each step execution and each recurring schedule is claimed by exactly one worker. No additional coordination needed.

**Scale the API horizontally** — it is stateless (all state is in the database). Run multiple instances behind a load balancer. Idempotency handling and retry locking both rely on database constraints and row locks, safe under concurrent instances.
