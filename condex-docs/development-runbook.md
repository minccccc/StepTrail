# Development Runbook

This document covers local setup, startup behavior, and a few important operational notes.

## Local Prerequisites

- .NET 9 SDK
- PostgreSQL
- optional Docker for local PostgreSQL
- VS Code if you want to use the provided debug configuration

## Repository Shape

Relevant top-level folders:

- `src`
- `docs`
- `condex-docs`

Per your repository convention, this document set lives in `condex-docs` and does not modify `docs`.

## Configuration

### API

File:

- `src/StepTrail.Api/appsettings.json`

Key settings:

- `ConnectionStrings:StepTrailDb`

### Worker

File:

- `src/StepTrail.Worker/appsettings.json`

Key settings:

- `ConnectionStrings:StepTrailDb`
- `Worker:PollIntervalSeconds`

Default poll interval:

- `5` seconds

## Database Startup

The repository currently assumes a PostgreSQL instance is available at:

```text
Host=localhost;Port=5432;Database=steptrail;Username=steptrail;Password=steptrail
```

There is no repo-owned Docker Compose file in the current solution, so local database startup is currently an external responsibility.

Example Docker command:

```bash
docker run -d ^
  --name steptrail-postgres ^
  -e POSTGRES_DB=steptrail ^
  -e POSTGRES_USER=steptrail ^
  -e POSTGRES_PASSWORD=steptrail ^
  -p 5432:5432 ^
  postgres:16
```

## Running The Solution

### Option 1: VS Code

The workspace contains debug configurations for:

- `StepTrail.Api`
- `StepTrail.Worker`
- compound launch: `StepTrail: API + Worker`

This is the easiest way to run both processes together.

### Option 2: Terminal

API:

```bash
cd src/StepTrail.Api
dotnet run
```

Worker:

```bash
cd src/StepTrail.Worker
dotnet run
```

## Startup Behavior

### API startup

When the API starts, it:

1. builds the web app
2. exposes OpenAPI and Scalar
3. applies pending EF Core migrations
4. seeds the default tenant if missing
5. syncs registered workflows into database metadata
6. starts serving HTTP traffic

### Worker startup

When the worker starts, it:

1. builds the host
2. registers step handlers
3. starts polling for due step executions

## Migrations

Migrations live in:

- `src/StepTrail.Api/Migrations`

The API applies pending migrations automatically on startup.

This keeps local setup simpler because there is no separate migration execution step before running the app.

## Operational Checks

Useful checks while developing:

- `GET /health`
- `GET /workflows`
- `GET /workflow-instances`
- `GET /workflow-instances/{id}/timeline`

Useful UI entry point:

- `http://localhost:5000/scalar/v1`

## Current Known Limitations

These are worth knowing before building on top of the current code:

- there are no automated test projects in the solution yet
- workflow replay currently starts from the first step, not an arbitrary chosen step
- step input propagation is simple and currently oriented around stored JSON payloads
- stale `Running` work recovery is not yet documented as a dedicated recovery mechanism
- the database is assumed to be available externally; local DB provisioning is not yet owned by the repo

## Recommended Next Use Of These Docs

If you are:

- implementing backend changes:
  start with `architecture-overview.md` and `runtime-and-lifecycle.md`
- building a UI:
  start with `api-and-integration.md`
- debugging production-like behavior locally:
  start with `data-model.md` and the timeline endpoints
