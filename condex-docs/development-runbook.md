# Development Runbook

This document covers local setup, startup behavior, useful URLs, and current operational notes for the current StepTrail solution.

## Local Prerequisites

- .NET 9 SDK
- PostgreSQL
- optional: Docker for local PostgreSQL
- optional: VS Code

## Repository Shape

Relevant top-level folders:

- `src` - solution projects
- `docs` - product and authoring guides
- `condex-docs` - code-aligned implementation docs

## Configuration

### API

File: `src/StepTrail.Api/appsettings.json`

Key settings:

| Key | Default | Description |
|-----|---------|-------------|
| `ConnectionStrings:StepTrailDb` | local Postgres | Database connection string |
| `UI:ApiBaseUrl` | `http://localhost:5000` | Base URL used by `WorkflowApiClient` loopback calls |
| `Ops:Username` | `admin` | Ops console username |
| `Ops:Password` | `admin` | Ops console password |
| `ApiTriggerAuthentication:SharedSecret` | empty | Shared secret for API-trigger auth |
| `ApiTriggerAuthentication:AllowUnauthenticated` | false in production config | Explicit local/dev opt-out for API-trigger auth |

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
| `Worker:PollIntervalSeconds` | `5` | Poll loop interval |
| `Worker:HeartbeatIntervalSeconds` | `60` | Lease-renewal heartbeat interval |
| `Worker:DefaultLockExpirySeconds` | `300` | Initial claim lease window |
| `Alerts:WebhookUrl` | `""` | Optional outbound alert webhook |

## Database Startup

The default local connection string expects:

```text
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

If the container already exists:

```bash
docker start steptrail-postgres
```

## Running The Solution

### Option 1: VS Code

The workspace includes launch configurations for:

- `StepTrail.Api`
- `StepTrail.Worker`
- the combined API + Worker launch

### Option 2: Terminal

```bash
# API
cd src/StepTrail.Api && dotnet run

# Worker
cd src/StepTrail.Worker && dotnet run
```

## Startup Behavior

### API startup

When the API starts, it:

1. builds the web app
2. configures cookie auth and Razor Pages
3. applies pending EF Core migrations
4. seeds the default tenant if needed
5. syncs code-registered workflow descriptors into the template catalog / definition metadata
6. seeds development data when `ASPNETCORE_ENVIRONMENT=Development`
7. starts serving HTTP traffic

### Worker startup

When the worker starts, it:

1. builds the host
2. registers step executors and support services
3. enters the poll loop
4. on each loop:
   - dispatches due recurring schedules
   - recovers orphaned executions
   - claims and processes one due step execution

## Migrations

Migrations live in:

- `src/StepTrail.Api/Migrations/`

The API applies pending migrations automatically on startup.

| Migration | Change |
|-----------|--------|
| `AddExecutableDefinitionPersistence` | Executable workflow/trigger/step definition tables |
| `AddExecutableDefinitionSnapshotsToWorkflowInstances` | Link instances to executable definitions |
| `AddActiveWorkflowDefinitionVersionUniqueness` | Unique constraint on active definition versions |
| `AddTriggerDataToWorkflowInstance` | Trigger data on instances |
| `AddWebhookRouteKeyToExecutableDefinitions` | Webhook route key on definitions |
| `ScopeIdempotencyByWorkflowKey` | Idempotency scoped by workflow key |
| `AddExecutableRecurringSchedules` | Recurring schedules for executable definitions |
| `AddCronSupportToRecurringSchedules` | Cron expression support |
| `AddFailureClassificationToStepExecution` | Failure classification column |
| `AddRetryPolicyJsonColumns` | Retry policy JSON on steps and executions |
| `AddSourceTemplateMetadata` | Source template key/version tracking |

To add a migration:

```bash
dotnet ef migrations add YourMigrationName \
  --project src/StepTrail.Api \
  --startup-project src/StepTrail.Api
```

## Automated Verification

The solution has automated tests.

Run them with:

```bash
dotnet test StepTrail.sln
```

## Useful Endpoints

### Health and docs

- `GET /health`
- `GET /openapi/v1.json`
- `GET /scalar/v1`

### Template and workflow definition surfaces

- `GET /workflows` - registered template descriptors
- `GET /workflow-definitions` - list executable workflow definitions

### Instance and trail surfaces

- `GET /workflow-instances`
- `GET /workflow-instances/{id}`
- `GET /workflow-instances/{id}/trail` - structured trail with attempt history

### Secrets

- `GET /secrets`
- `PUT /secrets/{name}`
- `DELETE /secrets/{name}`

## Useful Local URLs

- Scalar UI: `http://localhost:5000/scalar/v1`
- Ops login: `http://localhost:5000/login`
- Ops console (Instances): `http://localhost:5000/ops/workflows`
- Workflow definitions: `http://localhost:5000/ops/definitions`
- Template catalog: `http://localhost:5000/ops/templates`

## First-Time Authoring Flows

### Create from template

1. Navigate to `http://localhost:5000/ops/templates`
2. Review the template previews showing full step configuration
3. Click **Use Template** on any template
4. Fill in name, key, and trigger type, then click **Create Workflow**
5. You are redirected to the workflow editor where you can configure trigger details and step settings
6. Click **Activate** when ready, then trigger the workflow

### Create manually

1. Open `http://localhost:5000/ops/definitions`
2. Click **+ New Workflow**
3. Enter the workflow name and key
4. Choose the initial trigger type
5. Configure trigger and steps in the workflow editor
6. Activate the workflow

## Trigger Testing Notes

### Webhook-triggered workflows

Webhook routes are now keyed by the configured webhook route key, not by the workflow key.

Example:

```bash
curl -X POST http://localhost:5000/webhooks/my-route-key \
  -H "Content-Type: application/json" \
  -d '{"event":"test"}'
```

### Manual instance start page

The instances page includes:

- `/ops/workflows/create`

Use it to start a new workflow instance for testing from the operations console.

## Current Notes And Caveats

- workflow authoring is now split into Templates, Workflows, and Instances in the UI
- template-based workflow creation stores source-template metadata
- manual workflow creation currently starts from a workflow draft that must contain at least one step definition
- workflow definition validation on activation checks for required fields but does not validate runtime reachability of HTTP endpoints
- replay behavior is still a known area of active refinement; the current implementation remains more permissive than the intended long-term replay model
- API-trigger auth should be configured explicitly outside local development
- secrets are global, not tenant-scoped
- the committed ops credentials should be overridden in any non-local environment
