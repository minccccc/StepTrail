# StepTrail

StepTrail is a lightweight, database-backed workflow orchestration engine built on .NET 9 and PostgreSQL.

Today it runs as:

- one ASP.NET Core API host
- one .NET worker host
- one PostgreSQL database

Current capabilities include:

- code-first workflow definitions with DB sync
- idempotent workflow start
- durable step execution with row locking
- retries, replay, cancel, and archive operations
- worker lease renewal and orphan recovery
- recurring workflow dispatch
- webhook-triggered starts
- HTTP activity steps with stored response output
- workflow secrets and alert fanout
- a basic authenticated operations console
- a first packaged template: `webhook-to-http-call`

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
- Ops console: `http://localhost:5000/ops/workflows`

Important local note:

- the committed ops credentials are `admin / admin`; override them outside development
