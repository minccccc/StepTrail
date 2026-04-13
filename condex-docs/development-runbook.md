# Development Runbook

This document covers local setup, startup behavior, configuration, and current operational notes.

## Local Prerequisites

- .NET 9 SDK
- PostgreSQL (or Docker for local PostgreSQL)
- VS Code (optional — for the provided debug configuration)

## Repository Shape

Relevant top-level folders:

- `src` — solution projects
- `docs` — architecture and authoring guides
- `condex-docs` — code-aligned documentation set (this folder)

## Configuration

### API

File: `src/StepTrail.Api/appsettings.json`

Key settings:

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:StepTrailDb` | local Postgres | Database connection string |
| `UI:ApiBaseUrl` | `http://localhost:5000` | Base URL for `WorkflowApiClient` loopback calls |
| `Ops:Username` | `admin` | Ops console login username |
| `Ops:Password` | `admin` | Ops console login password |

Override credentials in non-local environments:

```bash
export Ops__Username=myuser
export Ops__Password=a-strong-password
```

### Worker

File: `src/StepTrail.Worker/appsettings.json`

Key settings:

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:StepTrailDb` | local Postgres | Database connection string |
| `Worker:PollIntervalSeconds` | `5` | Seconds between polling loop iterations |
| `Worker:HeartbeatIntervalSeconds` | `60` | Seconds between `StepLeaseRenewer` heartbeats |
| `Worker:DefaultLockExpirySeconds` | `300` | Lock window granted on claim; renewed each heartbeat |
| `Alerts:WebhookUrl` | `""` | Set to a non-empty URL to enable webhook failure alerts |

## Database Startup

The repository assumes a PostgreSQL instance at:

```
Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail
```

Example Docker command:

```bash
docker run -d \
  --name steptrail-postgres \
  -e POSTGRES_DB=steptrail \
  -e POSTGRES_USER=steptrail \
  -e POSTGRES_PASSWORD=steptrail \
  -p 5432:5432 \
  postgres:16
```

If the container already exists: `docker start steptrail-postgres`

## Running The Solution

### Option 1: VS Code

The workspace contains debug configurations for:

- `StepTrail.Api`
- `StepTrail.Worker`
- compound launch: `StepTrail: API + Worker`

This is the easiest way to run both processes together.

### Option 2: Terminal

```bash
# API (applies DB migrations automatically)
cd src/StepTrail.Api && dotnet run

# Worker (separate terminal)
cd src/StepTrail.Worker && dotnet run
```

## Startup Behavior

### API startup

When the API starts, it:

1. builds the web app
2. exposes OpenAPI and Scalar
3. registers cookie authentication
4. applies pending EF Core migrations
5. seeds the default tenant if missing
6. syncs registered workflows and recurring schedules into database metadata
7. seeds sample data if `ASPNETCORE_ENVIRONMENT=Development` (`DevDataSeedService`)
8. starts serving HTTP traffic

### Worker startup

When the worker starts, it:

1. builds the host
2. registers step handlers and supporting services
3. starts the polling loop — each iteration:
   - dispatches due recurring workflow schedules
   - recovers orphaned executions with expired leases
   - claims and processes one due step execution

## Migrations

Migrations live in `src/StepTrail.Api/Migrations/`.

The API applies pending migrations automatically on startup. No manual migration step is needed for local development.

To add a new migration after changing entities:

```bash
dotnet ef migrations add YourMigrationName \
  --project src/StepTrail.Api \
  --startup-project src/StepTrail.Api
```

Current migrations:

| Migration | Change |
|-----------|--------|
| `InitialSchema` | Base tables and constraints |
| `RemoveTenantFromWorkflowDefinition` | Definitions made global |
| `AddRetryPolicyToWorkflowDefinitionStep` | `max_attempts`, `retry_delay_seconds` |
| `AddTimeoutAndLockExpiry` | `timeout_seconds`, `lock_expires_at`, `started_at` |
| `AddRecurringWorkflowSchedules` | `recurring_workflow_schedules` table |
| `AddStepConfig` | `config` on definition steps |
| `AddWorkflowSecrets` | `workflow_secrets` table |

## Operational Checks

Useful endpoints while developing:

- `GET /health` — database connectivity
- `GET /workflows` — registered workflow definitions
- `GET /workflow-instances` — instance list
- `GET /workflow-instances/{id}/timeline` — event history for one instance
- `GET /secrets` — list configured secret names

Useful local URLs:

- Scalar UI: `http://localhost:5000/scalar/v1`
- Ops login: `http://localhost:5000/login` (default: `admin` / `admin`)
- Ops console: `http://localhost:5000/ops/workflows`
- Template setup: `http://localhost:5000/ops/templates`

## First-Time Template Setup

To test the `webhook-to-http-call` template end-to-end:

1. Navigate to `http://localhost:5000/ops/templates`
2. Click **Set up & Run** on the Webhook → HTTP Call card
3. Enter a target URL (e.g. a `https://webhook.site` receiver) and click **Save & Run Now**
4. The wizard saves the secret and starts an instance — you are redirected to the details page

For subsequent triggers without the wizard:

```bash
curl -X POST http://localhost:5000/webhooks/webhook-to-http-call \
  -H "Content-Type: application/json" \
  -d '{"event": "test"}'
```

## Current Known Limitations

These are worth knowing before building on top of the current code:

- there are no automated test projects in the solution
- workflow replay currently restarts from step 1 only; there is no arbitrary-step replay
- step timeout enforcement is cooperative — a handler that ignores the `CancellationToken` cannot be force-terminated; orphan recovery only kicks in after the lock expires (default 5 minutes)
- if `RecurrenceIntervalSeconds` is added to an existing workflow descriptor without bumping the version, the recurring schedule is not created (the definition already exists and the sync is skipped); bump the version to trigger re-sync
- if a `recurring_workflow_schedules` row is manually deleted from the database, it is not automatically recreated on the next startup for an already-synced definition
- the committed ops credentials (`admin`/`admin`) must be overridden via `Ops__Username` and `Ops__Password` environment variables before any non-local deployment
- secrets are global — not tenant-scoped
- the webhook endpoint silently discards malformed JSON bodies and starts the workflow with null input rather than returning `400 Bad Request`
