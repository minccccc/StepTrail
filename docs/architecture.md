# StepTrail — Architecture

## Overview

StepTrail is a workflow orchestration engine. It allows you to define multi-step background processes as code-first workflow descriptors, start instances of those workflows via a REST API, and execute each step reliably through a background worker service — with automatic retries, full event history, and manual recovery operations.

The system is intentionally simple: **PostgreSQL is the coordination layer**. There is no message queue, no in-process scheduler, no distributed cache. All state lives in the database, and workers coordinate via database-level row locking.

---

## System Diagram

```
┌─────────────────────────────────────────────────────────────┐
│                        Clients                               │
│              (REST calls, other services, UI)                │
└────────────────────────┬────────────────────────────────────┘
                         │ HTTP
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                     StepTrail.Api                            │
│                                                              │
│  Startup services:                                           │
│    TenantSeedService          (seeds dev tenant)             │
│    WorkflowDefinitionSyncService  (code → DB sync)           │
│                                                              │
│  Request services:                                           │
│    WorkflowInstanceService    (start, idempotency)           │
│    WorkflowQueryService       (list, detail, timeline)       │
│    WorkflowRetryService       (retry, replay)                │
│                                                              │
│  8 REST endpoints                                            │
│  Scalar UI at /scalar/v1                                     │
└────────────────────────┬────────────────────────────────────┘
                         │ EF Core / Npgsql
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                      PostgreSQL                              │
│                                                              │
│   tenants                 workflow_definitions               │
│   users                   workflow_definition_steps          │
│   workflow_instances      workflow_step_executions           │
│   workflow_events         idempotency_records                │
└────────────────────────┬────────────────────────────────────┘
                         │ EF Core / Npgsql
                         ▼
┌─────────────────────────────────────────────────────────────┐
│                    StepTrail.Worker                          │
│                                                              │
│  Worker (BackgroundService)                                  │
│    └── StepExecutionClaimer   (SELECT FOR UPDATE SKIP LOCKED)│
│    └── StepExecutionProcessor (resolve handler, run, persist)│
│                                                              │
│  Step Handlers (keyed DI):                                   │
│    SendWelcomeEmailHandler                                   │
│    ProvisionAccountHandler                                   │
│    NotifyTeamHandler                                         │
└─────────────────────────────────────────────────────────────┘
```

---

## Projects

### StepTrail.Shared

A class library referenced by both the API and the Worker. Contains everything that needs to be shared:

- **Domain entities** — the 8 EF Core entity classes that map to DB tables
- **Entity configurations** — EF Core Fluent API config (column names, constraints, indexes)
- **DbContext** — `StepTrailDbContext` with all 8 `DbSet<T>` properties
- **Workflow abstractions** — `WorkflowDescriptor`, `WorkflowStepDescriptor`, `IWorkflowRegistry`, `IStepHandler`, `StepContext`, `StepResult`
- **DI helpers** — extension methods `AddWorkflow<T>()`, `AddWorkflowRegistry()`, `AddStepTrailDb()`

Nothing in Shared has any business logic. It is pure infrastructure and contracts.

### StepTrail.Api

An ASP.NET Core Minimal API application. Responsibilities:

- **Schema ownership** — holds EF Core migrations, applies them on startup via `db.Database.MigrateAsync()`
- **Workflow registry** — syncs code-first workflow definitions to the DB at startup
- **Instance lifecycle** — starting instances, idempotency, manual retry/replay
- **Read API** — paginated list, instance detail, timeline queries
- **API documentation** — OpenAPI spec + Scalar UI

The API does not execute steps. It only creates and queries state.

### StepTrail.Worker

A .NET Worker Service (`BackgroundService`). Responsibilities:

- **Polling** — continuously checks for `Pending` step executions that are due (`scheduled_at <= now`)
- **Claiming** — atomically claims one execution at a time using `SELECT ... FOR UPDATE SKIP LOCKED`
- **Executing** — resolves the step handler by type name, runs it, captures output or error
- **Persisting** — writes the result back (success or failure), schedules the next step or retry, or marks the workflow complete/failed

The Worker has no REST endpoints and does not own the database schema.

---

## Key Design Decisions

### 1. PostgreSQL as the Coordination Layer

There is no message queue (no RabbitMQ, no Azure Service Bus, no Kafka). The database is the source of truth and the coordination mechanism. Workers compete to claim rows using `SELECT ... FOR UPDATE SKIP LOCKED`, which is a battle-tested pattern for work queues directly in PostgreSQL.

**Benefits:**
- No additional infrastructure to operate
- Transactional — claiming and writing results happen in the same DB transaction
- Simple operational model — if something goes wrong, you can inspect and fix state directly in the database

**Trade-offs:**
- Throughput is bounded by database polling (suitable for hundreds/thousands of executions per minute, not millions)
- All workers must be able to reach PostgreSQL

### 2. Code-First Workflow Definitions

Workflows are defined as C# classes that extend `WorkflowDescriptor`. They are not stored as YAML, JSON, or BPMN. At startup, `WorkflowDefinitionSyncService` reads all registered descriptors from the DI container and idempotently writes them to the `workflow_definitions` and `workflow_definition_steps` tables.

This means workflow definitions are **versioned in source control**, not in the database. The database copy is a derived artifact — a projection of the code.

```csharp
// Example: src/StepTrail.Api/Workflows/UserOnboardingWorkflow.cs
public sealed class UserOnboardingWorkflow : WorkflowDescriptor
{
    public override string Key => "user-onboarding";
    public override int Version => 1;
    public override string Name => "User Onboarding";
    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new("send-welcome-email",  nameof(SendWelcomeEmailHandler),  order: 1),
        new("provision-account",   nameof(ProvisionAccountHandler),  order: 2),
        new("notify-team",         nameof(NotifyTeamHandler),         order: 3)
    ];
}
```

When you need to change a workflow, bump the `Version`. Old and new versions can coexist in the database; `FindLatest` always picks the highest version.

### 3. Idempotency

`POST /workflow-instances` accepts an optional `IdempotencyKey`. If a request arrives with a key that has already been used by the same tenant, the existing instance is returned instead of creating a new one.

This is implemented via:
1. A `UNIQUE` constraint on `(tenant_id, idempotency_key)` in `idempotency_records`
2. A pre-check before insert (handles the common case)
3. A catch on `PostgresException { SqlState: "23505" }` after insert (handles the concurrent race case)

The database constraint is the safety net; the pre-check is the fast path.

### 4. Distributed Worker Safety

Multiple workers can run simultaneously without any additional coordination. The key is the PostgreSQL advisory-lock pattern:

```sql
SELECT * FROM workflow_step_executions
WHERE status = 'Pending' AND scheduled_at <= now()
ORDER BY scheduled_at ASC
LIMIT 1
FOR UPDATE SKIP LOCKED
```

`FOR UPDATE` places a row-level lock. `SKIP LOCKED` means concurrent workers skip already-locked rows instead of waiting. Each worker atomically claims exactly one execution.

### 5. Step-to-Step Data Flow

Step handlers receive a `StepContext` containing the previous step's output as `Input` (a JSON string). When a step succeeds, its `Output` becomes the `Input` of the next step execution row created in the database. Handlers are stateless — all state is passed through the database.

```
step 1 handler → Output: "{ userId: 42 }"
                          ↓ stored in DB
step 2 handler ← Input:  "{ userId: 42 }"
```

### 6. Event Log

Every state transition appends a row to `workflow_events`. Events are append-only and never deleted. The full timeline of a workflow instance — from start to completion, including retries — is always queryable via `GET /workflow-instances/{id}/timeline`.

Event types: `WorkflowStarted`, `StepStarted`, `StepCompleted`, `StepFailed`, `StepRetryScheduled`, `WorkflowCompleted`, `WorkflowFailed`, `WorkflowRetried`, `WorkflowReplayed`

### 7. Retry Policy

Each step definition carries its own `MaxAttempts` (default: 3) and `RetryDelaySeconds` (default: 30). These are set at workflow definition time, not at runtime.

When a step fails:
- If `attempt < MaxAttempts`: a new `WorkflowStepExecution` row is created with `attempt + 1` and `ScheduledAt = now + RetryDelaySeconds`. The worker will pick it up after the delay.
- If `attempt == MaxAttempts`: the workflow instance is marked `Failed`.

Manual retry (`POST /workflow-instances/{id}/retry`) resets the attempt counter to 1 and reschedules from the last failed step. Manual replay (`POST /workflow-instances/{id}/replay`) resets to step 1.

Both manual operations use `SELECT ... FOR UPDATE` (without `SKIP LOCKED`) to prevent concurrent duplicates — a second request blocks until the first commits, then sees the updated state and returns 409 Conflict.

---

## Execution Lifecycle

```
Client                    API                      DB                       Worker
  │                        │                        │                          │
  │  POST /workflow-       │                        │                          │
  │  instances ──────────► │                        │                          │
  │                        │  validate tenant       │                          │
  │                        │  check idempotency ──► │                          │
  │                        │  insert instance       │                          │
  │                        │  insert step exec ──── ▼                          │
  │                        │  insert event    (Pending, Attempt 1)             │
  │  201 Created ◄──────── │                        │                          │
  │                        │                        │  poll (every 5s) ◄────── │
  │                        │                        │  SELECT FOR UPDATE       │
  │                        │                        │  SKIP LOCKED ──────────► │
  │                        │                        │  (claims execution)      │
  │                        │                        │  step → Running ◄─────── │
  │                        │                        │  instance → Running      │
  │                        │                        │  StepStarted event       │
  │                        │                        │                          │
  │                        │                        │  handler.ExecuteAsync()  │
  │                        │                        │  [success]               │
  │                        │                        │  step → Completed ◄───── │
  │                        │                        │  Output stored           │
  │                        │                        │  StepCompleted event     │
  │                        │                        │  next step inserted      │
  │                        │                        │  (or instance Completed) │
  │                        │                        │                          │
  │  GET /workflow-        │                        │                          │
  │  instances/{id} ─────► │  query DB ───────────► │                          │
  │  (Completed) ◄──────── │ ◄───────────────────── │                          │
```

### Failure Path

```
  [handler throws exception]
  step → Failed  ◄──────────────────────────────── Worker
  StepFailed event
  if attempt < MaxAttempts:
    new execution (Attempt+1, ScheduledAt = now+delay)
    StepRetryScheduled event
  else:
    instance → Failed
    WorkflowFailed event
```

---

## Database Schema

### Entity Relationship

```
tenants
  ├── users                     (tenant_id FK)
  ├── workflow_instances         (tenant_id FK)
  │     ├── workflow_step_executions  (workflow_instance_id FK)
  │     └── workflow_events           (workflow_instance_id FK)
  └── idempotency_records        (tenant_id FK)

workflow_definitions
  ├── workflow_definition_steps  (workflow_definition_id FK)
  └── workflow_instances         (workflow_definition_id FK)
```

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
| `step_type` | `varchar(500)` | Handler class name (keyed DI key) |
| `order` | `int` | UNIQUE per definition, 1-based |
| `max_attempts` | `int` | Default 3 |
| `retry_delay_seconds` | `int` | Default 30 |
| `created_at` | `timestamptz` | |

#### `workflow_instances`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `tenant_id` | `uuid` | FK → tenants |
| `workflow_definition_id` | `uuid` | FK → workflow_definitions |
| `external_key` | `varchar(500)` | nullable, caller-provided business key |
| `idempotency_key` | `varchar(500)` | nullable, deduplication key |
| `status` | `varchar(50)` | Pending / Running / Completed / Failed / Cancelled |
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
| `output` | `jsonb` | nullable, populated on success |
| `error` | `text` | nullable, populated on failure |
| `scheduled_at` | `timestamptz` | when worker may pick it up |
| `locked_at` | `timestamptz` | nullable, when worker claimed it |
| `locked_by` | `varchar(200)` | nullable, worker ID |
| `started_at` | `timestamptz` | nullable, when handler began |
| `completed_at` | `timestamptz` | nullable |
| `created_at` | `timestamptz` | |
| `updated_at` | `timestamptz` | |

**Critical index:** `(status, scheduled_at)` — this is the index the worker uses for every poll.

#### `workflow_events`
| Column | Type | Notes |
|--------|------|-------|
| `id` | `uuid` | PK |
| `workflow_instance_id` | `uuid` | FK → workflow_instances |
| `step_execution_id` | `uuid` | nullable FK → workflow_step_executions |
| `event_type` | `varchar(100)` | see event type constants |
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

---

## Migrations

Migrations are EF Core code-first migrations living in `src/StepTrail.Api/Migrations/`. The API project owns the schema. The Worker reads and writes but never migrates.

| Migration | Change |
|-----------|--------|
| `20260410152221_InitialSchema` | All 8 tables, constraints, indexes |
| `20260410190826_RemoveTenantFromWorkflowDefinition` | Workflow definitions made global (removed tenant_id) |
| `20260410192716_AddRetryPolicyToWorkflowDefinitionStep` | Added `max_attempts` (default 3) and `retry_delay_seconds` (default 30) |

Migrations are applied automatically at API startup via `db.Database.MigrateAsync()`.

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
    "DefaultConnection": "Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail"
  }
}
```

### Worker (`src/StepTrail.Worker/appsettings.json`)

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail"
  },
  "Worker": {
    "PollIntervalSeconds": 5
  }
}
```

---

## Scaling

**Scale the Worker horizontally** — run multiple instances of `StepTrail.Worker` pointing at the same database. The `SELECT ... FOR UPDATE SKIP LOCKED` pattern ensures each step execution is claimed by exactly one worker. No additional coordination needed.

**Scale the API horizontally** — it is stateless (all state is in the database). Run multiple instances behind a load balancer. The idempotency handling and retry locking both rely on database constraints and row locks, so they are safe under concurrent API instances.
