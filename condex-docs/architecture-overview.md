# Architecture Overview

## Executive Summary

StepTrail is a modular monolith with two runtime hosts:

- `StepTrail.Api`
- `StepTrail.Worker`

Both hosts share the same PostgreSQL database and the same shared project:

- `StepTrail.Shared`

This is not a microservice system. It is one solution with separate runtime roles:

- the API serves a protected operations surface and a small public integration surface
- the worker performs background execution and recovery
- the database is the durable coordination mechanism between them

## Architectural Style

The current implementation is closer to a pragmatic modular monolith than to strict Clean Architecture.

Why:

- there is one business system and one repository
- workflow state lives in one relational database
- the API and worker are separate processes, but not separate bounded contexts
- `StepTrail.Shared` still mixes entity model, EF Core persistence, and shared workflow contracts

So the architecture is:

- modular in runtime responsibility
- monolithic in ownership and persistence
- explicit rather than heavily abstracted

## Runtime Topology

```text
            Internal Operators
                   |
                   v
        +-------------------------+
        |      StepTrail.Api      |
        |-------------------------|
        | - Razor Pages ops UI    |
        | - Cookie auth           |
        | - Ops API               |
        | - Template registry     |
        | - Definition CRUD       |
        | - Template catalog      |
        | - Definition sync       |
        | - Workflow authoring    |
        | - Start/retry/replay    |
        | - Cancel/archive        |
        | - Trail queries         |
        | - Secrets endpoints     |
        +-----------+-------------+
                    |
                    |
External Systems    |            +-------------------------+
      |             |            |    StepTrail.Worker     |
      v             v            |-------------------------|
 POST /webhooks  PostgreSQL <----| - Poll loop             |
                                 | - Lease-based claiming  |
                                 | - Lease renewal         |
                                 | - Orphan recovery       |
                                 | - Recurring dispatch    |
                                 | - Step execution        |
                                 | - Retry scheduling      |
                                 | - Alert fanout          |
                                 +-------------------------+
```

## Solution Responsibilities

### `src/StepTrail.Api`

Primary responsibilities:

- Minimal API endpoints
- Razor Pages operations UI
- cookie authentication for operators
- OpenAPI and Scalar exposure
- EF Core migration application on startup
- tenant seed for local development
- exposing the template catalog from code-registered workflow descriptors
- syncing code-first workflow descriptors into database metadata
- creating and editing executable workflow definitions
- workflow definition CRUD (create, edit, activate/deactivate, clone)
- structured trail view endpoint
- template catalog Razor Page
- three-section ops UI: Instances, Workflows, Templates
- workflow start command handling
- manual retry, replay, cancel, and archive operations
- operational query endpoints
- secrets management endpoints
- public webhook trigger endpoint

This project is the system's operator-facing and integration-facing entry point.

### `src/StepTrail.Worker`

Primary responsibilities:

- background polling loop
- claiming due `workflow_step_executions`
- lease extension while handlers run
- recovering orphaned `Running` executions with expired locks
- resolving step executors from DI
- running step logic
- writing execution results
- scheduling next steps
- scheduling retries
- failure classification of step execution errors
- retry policy resolution with configurable backoff (Fixed/Exponential)
- AwaitingRetry status transitions
- dispatching recurring workflow schedules
- sending alerts for workflow failure and orphan recovery

This project is the execution engine.

### `src/StepTrail.Shared`

Primary responsibilities:

- EF Core `DbContext`
- entity classes
- entity configuration classes
- workflow registration abstractions
- executable workflow contracts
- step executor contracts
- shared DI extensions
- workflow definitions domain model (WorkflowDefinition, TriggerDefinition, StepDefinition)
- RetryPolicy model with BackoffStrategy
- FailureClassification enum
- WorkflowDefinitionStatus enum (Draft, Active, Inactive, Archived)
- executable definition persistence (ExecutableWorkflowDefinitionRecord, ExecutableTriggerDefinitionRecord, ExecutableStepDefinitionRecord)

This project acts as the shared contract and persistence layer for both hosts.

## Capability Map

From a system-capability point of view, the current solution groups into these areas:

### Workflow Definitions

- define templates in code
- register templates at startup
- persist executable workflow definitions to the database
- support manual and template-based workflow creation
- create workflow definitions (blank, from template, clone)
- edit trigger and step configurations via UI
- activate/deactivate definitions
- source template tracking
- optionally attach recurrence metadata to a definition

### Workflow Runtime

- start workflow instances
- create initial step executions
- claim and run step executions
- schedule next steps
- retry on failure
- detect orphaned executions
- attempt timeout-aware execution

### Triggering And Integration

- protected start endpoint for operators
- public webhook-triggered start endpoint
- HTTP activity step for outbound calls
- secret resolution in step configuration
- alert fanout through log and optional webhook channels

### Operations Console

- three sections: Instances list/detail, Workflow definition editor, Template catalog
- structured trail view with attempt history
- action eligibility (CanRetry, CanReplay, CanCancel, CanArchive)
- login/logout
- manual retry, replay, cancel, and archive actions

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

### 2. Code-first templates plus persisted workflow definitions

Templates are defined as C# descriptors, then synchronized into database metadata.

Users do not edit or execute those descriptors directly. They create persisted workflow definitions from templates or manually through the authoring UI.

That gives:

- versioned template blueprints in code
- persisted workflow definitions for runtime and read-side behavior
- simple registration without a dynamic builder or plugin model

### 3. Append-oriented execution history

The system preserves execution history by appending new `workflow_step_executions` rows instead of mutating one row forever.

That is important for:

- retries
- replay
- timeline visibility
- debugging

### 4. Protected ops surface plus public trigger surface

The operations UI and ops API are cookie-protected and intended for internal use.

Separately, the webhook trigger endpoint is intentionally public and narrow:

- it only starts workflows
- it delegates to the same start logic
- it uses headers for idempotency and external correlation

### 5. Constrained workflow authoring plus simple configuration

Workflow authoring remains intentionally form-driven:

- one trigger per workflow
- ordered steps
- explicit trigger and step-type forms
- no drag-and-drop graph builder

That keeps the product model understandable while still supporting both template-based and manual creation flows.

### 6. Simple alerting model

External step configuration remains explicit:

- step config JSON is stored on the definition step
- secrets are stored in the database and resolved by placeholder
- alerts fan out through pluggable channels, currently log and optional webhook

This keeps the implementation minimal while still supporting real integrations.

## Startup Sequence

### API startup

1. Build the DI container.
2. Register cookie auth, Razor Pages, and the loopback `WorkflowApiClient`.
3. Register OpenAPI and Scalar.
4. Register database access.
5. Register workflow descriptors and workflow registry.
6. Register hosted startup services.
7. Apply pending EF Core migrations.
8. Start hosted services:
   `TenantSeedService` seeds the default tenant if missing.
   `WorkflowDefinitionSyncService` syncs registered workflows into the database.
   `DevDataSeedService` seeds UI/demo data in development.
9. Begin serving HTTP requests.

### Worker startup

1. Build the DI container.
2. Register database access.
3. Register workflow registry.
4. Register step executors.
5. Register alert channels and supporting services.
6. Start the background loop.
7. Each loop iteration:
   recover orphaned executions
   dispatch due recurring schedules
   claim one due step execution
   process it

## Architectural Strengths

- clear split between operator API/UI and background execution
- durable execution state
- explicit lifecycle transitions
- template catalog and workflow authoring without introducing a plugin framework
- recoverable worker model based on row locking and lease expiry
- simple mental model for debugging through instance, steps, events, schedules, and secrets

## Architectural Tradeoffs

- `StepTrail.Shared` is not a pure domain core; it mixes persistence and shared contracts
- the Razor Pages UI still talks to the same host through loopback HTTP rather than direct in-process services
- workflow secrets are stored in plaintext
- timeout handling is partly cooperative: a handler that ignores cancellation can still remain `Running`
- the system depends heavily on database coordination, so schema quality and indexes matter a lot
- some workflow authoring and replay semantics are still in transition, so product design is slightly ahead of a few runtime details
- the solution includes unit and integration tests using Testcontainers for PostgreSQL

## Bottom Line

The architecture is still sensible for the current scope.

It favors:

- clarity
- explicit persistence
- operational visibility
- incremental growth

That remains a good fit for a workflow engine being built through staged PBIs, even though some areas are intentionally simple and not yet hardened.
