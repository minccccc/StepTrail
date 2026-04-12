# StepTrail

StepTrail is a lightweight, database-backed workflow orchestration engine built on .NET 9 and PostgreSQL. It lets you define multi-step async workflows in code, execute them reliably with automatic retries, and observe every state transition through a full event timeline.

## What It Does

- Define workflows as ordered sequences of steps in C# code
- Start workflow instances via REST API, with full idempotency support
- Execute steps automatically via a background worker with `SELECT ... FOR UPDATE SKIP LOCKED` — safe for multiple concurrent workers
- Retry failed steps automatically (configurable per step) or manually via API
- Replay any workflow from the beginning
- Query instance status, step detail, and a full event timeline at any time
- Multi-tenant: all instances are scoped to a tenant; workflow definitions are shared globally

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Runtime | .NET 9 |
| API Framework | ASP.NET Core Minimal API |
| Worker | .NET Worker Service (`BackgroundService`) |
| ORM | Entity Framework Core 9 |
| Database | PostgreSQL (via Npgsql) |
| API Docs | OpenAPI + Scalar UI |

---

## Project Structure

```
StepTrail/
├── src/
│   ├── StepTrail.Shared/          # Domain entities, EF context, workflow abstractions
│   ├── StepTrail.Api/             # REST API, migrations, startup services
│   └── StepTrail.Worker/          # Background worker, step execution, handlers
├── docs/
│   ├── architecture.md            # System design, patterns, data flow
│   └── workflow-authoring.md      # How to define workflows and implement handlers
├── .vscode/
│   ├── launch.json                # Debug configs (API, Worker, compound)
│   └── tasks.json                 # Build task
└── StepTrail.sln
```

See [docs/architecture.md](docs/architecture.md) for a full architectural breakdown.

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- [Docker](https://www.docker.com/) (for PostgreSQL)

---

## Quick Start

### 1. Start PostgreSQL

```bash
docker run -d \
  --name steptrail-postgres \
  -e POSTGRES_DB=steptrail \
  -e POSTGRES_USER=steptrail \
  -e POSTGRES_PASSWORD=steptrail \
  -p 5432:5432 \
  postgres:16
```

If you already have the container:

```bash
docker start steptrail-postgres
```

### 2. Run via VS Code

Open the project in VS Code, then use the **Run and Debug** panel and launch **StepTrail: API + Worker** — this starts both processes simultaneously.

### 3. Run via Terminal

In two separate terminals:

```bash
# Terminal 1 — API (applies DB migrations automatically on first run)
cd src/StepTrail.Api
dotnet run

# Terminal 2 — Worker
cd src/StepTrail.Worker
dotnet run
```

### 4. Explore the API

Open the Scalar UI: **http://localhost:5000/scalar/v1**

Or hit the health endpoint to verify everything is connected:

```bash
curl http://localhost:5000/health
```

---

## API Reference

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/health` | Database connectivity check |
| `GET` | `/workflows` | List all registered workflow definitions |
| `POST` | `/workflow-instances` | Start a workflow instance |
| `GET` | `/workflow-instances` | List instances (paginated, filterable) |
| `GET` | `/workflow-instances/{id}` | Get instance detail with all step executions |
| `GET` | `/workflow-instances/{id}/timeline` | Get full event timeline for an instance |
| `POST` | `/workflow-instances/{id}/retry` | Retry from last failed step |
| `POST` | `/workflow-instances/{id}/replay` | Replay from step 1 |

### Start a Workflow

```http
POST /workflow-instances
Content-Type: application/json

{
  "workflowKey": "user-onboarding",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "externalKey": "user-42",
  "idempotencyKey": "onboard-user-42-v1",
  "input": { "userId": 42, "email": "user@example.com" }
}
```

`idempotencyKey` is optional. If provided, a second request with the same key returns the original instance instead of creating a duplicate.

### List Instances

```http
GET /workflow-instances?tenantId=00000000-0000-0000-0000-000000000001&status=Failed&page=1&pageSize=20
```

Supported `status` values: `Pending`, `Running`, `Completed`, `Failed`, `Cancelled`

---

## Database

Migrations are applied automatically when the API starts. There is no manual migration step.

The default connection string (see `src/StepTrail.Api/appsettings.json`):

```
Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail
```

---

## Adding Workflows

See [docs/workflow-authoring.md](docs/workflow-authoring.md) for a step-by-step guide to defining new workflows and implementing step handlers.

The built-in example is `UserOnboardingWorkflow` (3 steps: send welcome email → provision account → notify team), defined in `src/StepTrail.Api/Workflows/`.

---

## Documentation

| Document | Purpose |
|----------|---------|
| [docs/architecture.md](docs/architecture.md) | System design, execution flow, DB schema, key patterns |
| [docs/workflow-authoring.md](docs/workflow-authoring.md) | How to define workflows and implement handlers |
