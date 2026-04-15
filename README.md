# StepTrail

StepTrail is a lightweight, database-backed workflow orchestration engine built on .NET 9 and PostgreSQL.

Today it runs as:

- one ASP.NET Core API host
- one .NET worker host
- one PostgreSQL database

Current capabilities include:

- Three-level model: Templates (predefined blueprints) -> Workflow Definitions (user-owned, editable) -> Workflow Instances (runtime)
- Code-first workflow descriptors with DB sync (IWorkflowRegistry)
- UI-based workflow definition authoring (create blank, from template, or clone)
- All trigger types: Webhook, Manual, API, Schedule (interval + cron)
- All step types: HttpRequest, SendWebhook, Transform, Conditional, Delay
- RetryPolicy model with configurable backoff (Fixed/Exponential), max attempts, delay, timeout-aware retries
- Failure classification (Transient, Permanent, InvalidConfiguration, InputResolutionFailure)
- Durable step execution with row locking and lease renewal
- AwaitingRetry status for automatic retry scheduling
- Retry (from failed step), Replay (from step 1), Cancel, and Archive operations
- Structured trail view with attempt history
- Orphan recovery via lease expiry detection
- Recurring workflow dispatch (interval + cron)
- Webhook-triggered and API-triggered starts
- HTTP activity steps with stored response output
- Workflow secrets and placeholder resolution
- Alert fanout (console log + optional webhook)
- Authenticated operations console with three-section navigation
- Two packaged templates: user-onboarding, webhook-to-http-call
- Comprehensive test suite (unit + integration tests with Testcontainers)

## Start Here

The maintained documentation set lives in [condex-docs](condex-docs/README.md).

Recommended reading order:

1. [Architecture Overview](condex-docs/architecture-overview.md)
2. [Runtime And Lifecycle](condex-docs/runtime-and-lifecycle.md)
3. [Data Model](condex-docs/data-model.md)
4. [API And Integration Overview](condex-docs/api-and-integration.md)
5. [Development Runbook](condex-docs/development-runbook.md)

## Quick Start

Prerequisites:

- .NET 9 SDK
- PostgreSQL

Default local database connection:

```text
Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail
```

Run PostgreSQL with Docker if needed:

```bash
docker run -d \
  --name steptrail-postgres \
  -e POSTGRES_DB=steptrail \
  -e POSTGRES_USER=steptrail \
  -e POSTGRES_PASSWORD=steptrail \
  -p 5432:5432 \
  postgres:16
```

Run the API:

```bash
cd src/StepTrail.Api
dotnet run
```

Run the worker:

```bash
cd src/StepTrail.Worker
dotnet run
```

Useful local URLs:

- API docs: `http://localhost:5000/scalar/v1`
- Ops login: `http://localhost:5000/login`
- Ops console: `http://localhost:5000/ops/workflows` (Instances)
- Workflow definitions: `http://localhost:5000/ops/definitions`
- Template catalog: `http://localhost:5000/ops/templates`

Important local note:

- the committed ops credentials are `admin / admin`; override them outside development
