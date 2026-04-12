# Architecture Overview

## Executive Summary

StepTrail is best described as a modular monolith with two runtime hosts:

- `StepTrail.Api`
- `StepTrail.Worker`

Both hosts share the same PostgreSQL database and the same shared project:

- `StepTrail.Shared`

This means the system is not split into distributed services. Instead, it is one solution with clear runtime roles:

- the API accepts commands and serves operational reads
- the worker performs background execution
- the database is the durable coordination mechanism between them

## Architectural Style

The current implementation is closer to a pragmatic modular monolith than to strict Clean Architecture.

Why:

- there is a single deployable business system
- all workflow state is stored in one relational database
- the API and worker are separate processes, but not separate bounded contexts
- the shared project contains domain entities, EF Core persistence, and workflow contracts

So the architecture is:

- modular in runtime responsibility
- monolithic in system ownership and persistence
- explicit rather than heavily abstracted

## Runtime Topology

```text
                +----------------------+
                |   StepTrail.Api      |
                |----------------------|
                | - Minimal API        |
                | - Workflow registry  |
                | - Definition sync    |
                | - Start/retry/replay |
                | - Read-side queries  |
                +----------+-----------+
                           |
                           |
                           v
                +----------------------+
                |      PostgreSQL      |
                |----------------------|
                | workflow_definitions |
                | workflow_instances   |
                | step_executions      |
                | idempotency_records  |
                | workflow_events      |
                +----------+-----------+
                           ^
                           |
                           |
                +----------+-----------+
                |   StepTrail.Worker   |
                |----------------------|
                | - Poll loop          |
                | - Step claiming      |
                | - Handler execution  |
                | - Retry scheduling   |
                | - Completion/failure |
                +----------------------+
```

## Solution Responsibilities

### `src/StepTrail.Api`

Primary responsibilities:

- HTTP endpoints
- OpenAPI and Scalar UI exposure
- EF Core migration application on startup
- tenant seed for local development
- syncing code-first workflow descriptors into database metadata
- workflow start command handling
- manual retry and replay operations
- operational query endpoints

This project is the system's command and read entry point.

### `src/StepTrail.Worker`

Primary responsibilities:

- background polling loop
- claiming due `workflow_step_executions`
- resolving step handlers from DI
- running step logic
- writing execution results
- scheduling next steps
- scheduling retries
- finalizing workflow completion or failure

This project is the execution engine.

### `src/StepTrail.Shared`

Primary responsibilities:

- EF Core `DbContext`
- entity classes
- entity configuration classes
- workflow registration abstractions
- step handler contracts
- shared DI extensions

This project acts as the shared contract and persistence layer for both hosts.

## Capability Map

From a business-capability point of view, the solution currently groups into these areas:

### Workflow Definitions

- define workflows in code
- register them at startup
- persist them to the database

### Workflow Runtime

- start workflow instances
- create initial step execution
- claim and run step executions
- schedule next steps
- retry on failure
- manually retry or replay

### Operations / Observability

- list workflows
- list instances
- inspect instance detail
- inspect timeline / events

### Platform / Setup

- tenant seed
- migrations
- local environment startup

## Main Architectural Decisions

### 1. Database-first coordination

The worker does not coordinate execution in memory.

Instead:

- the API creates durable rows
- the worker polls durable rows
- step lifecycle is persisted in the database

This is the core design decision that makes the system restart-tolerant and inspectable.

### 2. Code-first workflow registration

Workflows are defined as C# descriptors, then synchronized into database metadata.

That gives:

- versioned workflow definitions in code
- database visibility for runtime and read-side behavior
- simple registration without a dynamic builder or plugin model

### 3. Explicit step execution history

The system preserves execution history by appending new `workflow_step_executions` rows instead of mutating a single row forever.

That is important for:

- retries
- replay
- timeline visibility
- debugging

### 4. Thin hosts, stateful database

Neither host owns authoritative workflow state in memory.

The hosts are process coordinators. The database is the source of truth.

## Startup Sequence

### API startup

1. Build the DI container.
2. Register OpenAPI.
3. Register database access.
4. Register workflow descriptors and workflow registry.
5. Register hosted startup services.
6. Apply pending EF Core migrations.
7. Start hosted services:
   `TenantSeedService` seeds the default tenant if missing.
   `WorkflowDefinitionSyncService` syncs registered workflows into the database.
8. Begin serving HTTP requests.

### Worker startup

1. Build the DI container.
2. Register database access.
3. Register workflow registry.
4. Register step handlers.
5. Start the background polling loop.

## Architectural Strengths

- clear split between command/read API and background execution
- durable execution state
- explicit lifecycle transitions
- simple mental model
- easy to inspect operational behavior from the database and API
- no speculative framework layers

## Architectural Tradeoffs

- `StepTrail.Shared` is not a pure domain core; it mixes persistence and shared contracts
- the system depends heavily on database coordination, so DB schema quality matters a lot
- workflow definitions are globally shared, while instance execution is tenant-scoped
- there are no separate test projects yet, so architectural guarantees are validated mostly by reading code and manual execution

## Bottom Line

The architecture is sensible for the current scope.

It favors:

- clarity
- explicit persistence
- operational visibility
- incremental growth

That is a good fit for a workflow engine built through staged PBIs.
