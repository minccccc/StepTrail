# StepTrail Architecture

## Overview

StepTrail is a workflow orchestration engine built as a modular monolith.

The current product model has three layers:

- **Templates** - code-registered workflow blueprints
- **Workflows** - persisted executable workflow definitions created from templates or manually
- **Workflow Instances** - concrete executions of workflows

At runtime, StepTrail is made up of:

- `StepTrail.Api`
- `StepTrail.Worker`
- one shared PostgreSQL database

PostgreSQL is the durable coordination layer. The system does not rely on a message queue, in-memory scheduler, or distributed cache.

## Runtime Topology

```text
Operators / External Callers
            |
            v
   +----------------------+
   |    StepTrail.Api     |
   |----------------------|
   | - Razor Pages UI     |
   | - Ops API            |
   | - Public triggers    |
   | - Template catalog   |
   | - Workflow authoring |
   | - Query/read models  |
   +----------+-----------+
              |
              v
        +-----------+
        | PostgreSQL|
        +-----------+
              ^
              |
   +----------+-----------+
   |   StepTrail.Worker   |
   |----------------------|
   | - Poll loop          |
   | - Claiming/leases    |
   | - Retry scheduling   |
   | - Waiting/delay      |
   | - Recurring dispatch |
   | - Orphan recovery    |
   | - Step execution     |
   +----------------------+
```

## Project Responsibilities

### `src/StepTrail.Api`

Responsibilities:

- Minimal API endpoints
- Razor Pages operations UI
- cookie-based operator authentication
- template catalog exposure from registered descriptors
- workflow definition CRUD and activation flows
- workflow instance start, retry, replay, cancel, archive
- read-side queries for workflow definitions, instances, and trail data
- public trigger intake for webhook and API starts
- secrets endpoints
- EF Core migration application on startup

### `src/StepTrail.Worker`

Responsibilities:

- polling for due work
- claiming step executions safely
- renewing execution leases
- recovering orphaned executions
- dispatching recurring schedules
- executing step types through the step executor registry
- scheduling retries and next steps
- persisting outputs and failure results

### `src/StepTrail.Shared`

Responsibilities:

- EF Core entity model and `StepTrailDbContext`
- persistence mappings
- template registration abstractions (`WorkflowDescriptor`, `WorkflowStepDescriptor`, `IWorkflowRegistry`)
- executable workflow definition model (`WorkflowDefinition`, `TriggerDefinition`, `StepDefinition`)
- retry policy and failure classification model
- shared runtime contracts used by API and worker

## Core Architectural Decisions

### 1. Database-backed orchestration

Workflow lifecycle is persisted explicitly:

- workflow definitions
- workflow instances
- step executions
- events / trail records
- recurring schedules
- secrets

That makes the system restart-tolerant and inspectable.

### 2. Templates are code-first, workflows are persisted

Templates are registered in code via `WorkflowDescriptor`.

Users do not edit those descriptors directly. Instead they:

- create a workflow from a template, or
- create a workflow manually

Those workflows are stored as persisted executable workflow definitions and then activated for runtime use.

### 3. One trigger, ordered steps

Each workflow definition currently has:

- exactly one trigger
- an ordered list of executable steps

The authoring model is intentionally constrained:

- no drag-and-drop canvas
- no arbitrary graph builder
- explicit trigger and step forms

### 4. Worker execution is lease-based

The worker uses durable claiming with database coordination.

Key behaviors:

- due work is claimed from the database
- running work renews its lease
- expired leases are treated as orphaned work
- retries are scheduled durably, not as in-process loops

### 5. Runtime owns retries, executors own step behavior

Step executors:

- perform one step-specific responsibility
- return normalized output or classified failure

The runtime:

- applies retry policy
- schedules the next attempt
- propagates workflow state
- writes trail and attempt history

### 6. Read models and operator actions are explicit surfaces

The UI is built on top of operations APIs and read models, not raw EF entity dumps.

That separation supports:

- instance list views
- trail/detail views
- retry / replay / cancel actions
- workflow authoring and activation screens

## Authoring Model

Current UI sections:

- `/ops/templates` - template catalog
- `/ops/definitions` - workflow definitions
- `/ops/workflows` - workflow instances

Supported creation modes:

- **Use Template** - copy a template into a new inactive workflow definition
- **Create Manually** - create a new inactive workflow definition without starting from a template

Both flows end in the same workflow editor:

- trigger selection and configuration
- step editing
- step add/remove/reorder
- per-step retry policy overrides
- activation / deactivation

## Trigger And Step Model

Supported trigger types:

- `Webhook`
- `Manual`
- `Api`
- `Schedule`

Supported step types:

- `HttpRequest`
- `SendWebhook`
- `Transform`
- `Conditional`
- `Delay`

## Public Integration Surface

Current public endpoints include:

- `GET /health`
- `POST /webhooks/{routeKey}`
- `POST /api-triggers/{workflowKey}`

Webhook routing is based on the configured webhook route key of the active workflow definition.

## Ops Surface

The protected operations surface includes:

- template catalog APIs
- workflow definition APIs
- workflow instance list/detail/trail APIs
- retry, replay, cancel, and archive actions
- secrets endpoints
- Razor Pages UI under `/ops/**`

## Operational Characteristics

Strengths:

- explicit persisted state
- recoverable worker model
- traceable retries and attempt history
- clear product separation between templates, workflows, and instances
- server-rendered operations UI backed by stable APIs

Tradeoffs:

- the system depends heavily on database coordination and schema quality
- secrets are stored in the database and should be treated accordingly
- some replay and authoring semantics are still being refined
- the shared project mixes persistence and shared contracts rather than being a pure domain core

## Bottom Line

The current architecture favors:

- clarity
- durability
- operational visibility
- constrained authoring
- incremental delivery

That is a good fit for the current StepTrail scope and the product direction reflected in the latest workflow authoring model.
