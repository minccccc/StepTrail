# Data Model

This document describes the main persistence model used by StepTrail.

## Overview

The database is both:

- the system of record for workflow runtime state
- the coordination mechanism between API and worker

## Entity Map

```text
tenants
  |
  +-- users
  |
  +-- workflow_instances
  |      |
  |      +-- workflow_step_executions
  |      |      |
  |      |      +-- workflow_events (optional step link)
  |      |
  |      +-- workflow_events (instance-level)
  |
  +-- idempotency_records
  |
  +-- recurring_workflow_schedules (via tenant_id)

workflow_definitions
  |
  +-- workflow_definition_steps
  |
  +-- workflow_instances
  |
  +-- recurring_workflow_schedules (via workflow_definition_id)

executable_workflow_definitions
  |
  +-- executable_trigger_definitions
  |
  +-- executable_step_definitions

workflow_secrets  (standalone — referenced by name from step configs)
```

## Core Tables

### `tenants`

Purpose:

- tenant partition for runtime data

Used by:

- users
- workflow instances
- idempotency records
- recurring workflow schedules

Note:

- workflow definitions are not tenant-scoped

### `users`

Purpose:

- basic tenant-scoped user data

Current role:

- present in schema
- not a major runtime participant in the workflow engine yet

### `workflow_definitions`

Purpose:

- durable representation of code-first workflow metadata

Important fields:

- `key`
- `version`
- `name`
- `description`
- `created_at`

Important rule:

- `(key, version)` is unique

Interpretation:

- a workflow definition is global metadata shared by all tenants

### `workflow_definition_steps`

Purpose:

- ordered steps belonging to a workflow definition

Important fields:

- `workflow_definition_id`
- `step_key`
- `step_type`
- `order`
- `max_attempts`
- `retry_delay_seconds`
- `timeout_seconds` — nullable; when set, the worker cancels the handler after this many seconds
- `config` — nullable jsonb; handler-specific configuration (e.g. `HttpActivityConfig`)

Important rules:

- `(workflow_definition_id, step_key)` is unique
- `(workflow_definition_id, order)` is unique

Interpretation:

- this table defines the static execution plan for a workflow version

### `executable_workflow_definitions`

Purpose:

- durable representation of an executable workflow definition, created via API or from a template

Important fields:

- `id` (uuid PK)
- `key` (varchar)
- `webhook_route_key` (varchar, nullable)
- `name` (varchar)
- `version` (int)
- `status` (WorkflowDefinitionStatus: Draft/Active/Inactive/Archived)
- `description` (varchar, nullable)
- `source_template_key` (varchar, nullable)
- `source_template_version` (int, nullable)
- `created_at_utc` (timestamptz)
- `updated_at_utc` (timestamptz)

Important rule:

- `(key, version)` is unique when `status = Active`

Interpretation:

- an executable workflow definition is a standalone, versioned workflow that can be triggered by webhooks, API calls, schedules, or manually
- when created from a template, `source_template_key` and `source_template_version` record the origin

### `executable_trigger_definitions`

Purpose:

- defines how an executable workflow can be triggered

Important fields:

- `id` (uuid PK)
- `workflow_definition_id` (FK -> executable_workflow_definitions)
- `type` (TriggerType: Webhook/Manual/Schedule)
- `configuration` (jsonb) -- type-specific config

Interpretation:

- each executable workflow definition can have one or more trigger definitions
- the `configuration` column holds trigger-type-specific settings (e.g. webhook path, cron expression)

### `executable_step_definitions`

Purpose:

- ordered steps belonging to an executable workflow definition

Important fields:

- `id` (uuid PK)
- `workflow_definition_id` (FK -> executable_workflow_definitions)
- `key` (varchar)
- `order` (int)
- `type` (StepType: HttpRequest/SendWebhook/Transform/Conditional/Delay)
- `configuration` (jsonb) -- type-specific config
- `retry_policy_override_key` (varchar, nullable)
- `retry_policy_json` (varchar, nullable)

Interpretation:

- defines the static execution plan for an executable workflow version
- `retry_policy_override_key` references a named retry policy; `retry_policy_json` allows inline override

### `workflow_instances`

Purpose:

- one concrete run of a workflow definition

Important fields:

- `tenant_id`
- `workflow_definition_id`
- `executable_workflow_definition_id` (uuid, nullable FK -> executable_workflow_definitions)
- `workflow_definition_key` (varchar, nullable)
- `workflow_definition_version` (int, nullable)
- `external_key`
- `idempotency_key`
- `status`
- `input`
- `trigger_data` (jsonb, nullable)
- `created_at`
- `updated_at`
- `completed_at`

Interpretation:

- this is the parent row for execution state
- instances may belong to a code-first `workflow_definition_id` or an executable `executable_workflow_definition_id`, but not both
- `trigger_data` captures the payload or context from the trigger that started the instance

### `workflow_step_executions`

Purpose:

- one concrete attempt of one workflow step

Important fields:

- `workflow_instance_id`
- `workflow_definition_step_id`
- `executable_step_definition_id` (uuid, nullable FK -> executable_step_definitions)
- `step_key`
- `step_order` (int, nullable)
- `step_type` (varchar, nullable)
- `step_configuration` (jsonb, nullable)
- `status`
- `attempt`
- `input`
- `output` -- populated on success; also populated on `HttpActivityHandler` failure with the HTTP response (status + body)
- `error`
- `retry_policy_json` (varchar, nullable)
- `failure_classification` (varchar, nullable)
- `scheduled_at` -- gating field: worker only claims rows where `scheduled_at <= now`
- `locked_at`
- `locked_by`
- `lock_expires_at` -- renewed by `StepLeaseRenewer` while handler is running; used by orphan detection
- `started_at`
- `completed_at`
- `created_at`
- `updated_at`

Important design points:

- retries and replay append new rows instead of erasing prior rows
- `lock_expires_at` is the heartbeat anchor for orphan detection -- if it lapses, the execution may be recovered
- for executable workflows, `step_configuration` and `retry_policy_json` are snapshotted at execution creation time so the execution is self-contained
- `failure_classification` records why a step failed (see StepExecutionFailureClassification enum)

This table is the heart of the runtime engine.

### `idempotency_records`

Purpose:

- prevent duplicate workflow starts for the same tenant and idempotency key

Important fields:

- `tenant_id`
- `idempotency_key`
- `workflow_instance_id`

Important rule:

- `(tenant_id, idempotency_key)` is unique

Interpretation:

- a lookup table from caller request identity to the created workflow instance

### `workflow_events`

Purpose:

- timeline / audit trail of workflow lifecycle changes

Important fields:

- `workflow_instance_id`
- `step_execution_id` (optional)
- `event_type`
- `payload`
- `created_at`

Interpretation:

- instance-level events have no step execution link
- step-level events point to a specific execution row

### `recurring_workflow_schedules`

Purpose:

- records a repeating trigger for a workflow definition

Important fields:

- `workflow_definition_id` -- unique; one schedule per definition
- `executable_workflow_key` (varchar, nullable) -- for executable definition schedules
- `tenant_id`
- `interval_seconds`
- `cron_expression` (varchar, nullable) -- alternative to interval_seconds
- `is_enabled` -- disabling pauses dispatch without deleting the schedule
- `input` -- optional; forwarded to each new instance as workflow input
- `last_run_at`
- `next_run_at` -- gating field for dispatcher; advanced by `interval_seconds` or cron after each fire

Index:

- `(is_enabled, next_run_at)` — used by `RecurringWorkflowDispatcher` on every loop

Interpretation:

- the dispatcher fires a new workflow instance whenever `next_run_at <= now` and `is_enabled = true`

### `workflow_secrets`

Purpose:

- stores named secret values for use in step configurations

Important fields:

- `name` — unique; used as the key in `{{secrets.name}}` placeholders
- `value` — the raw secret value, never returned by any API endpoint
- `description`
- `created_at`
- `updated_at`

Interpretation:

- `SecretResolver` (worker) loads secrets by name and substitutes them into step config strings at execution time
- the ops API (`GET /secrets`) returns names and descriptions only, never values

## Status Enums

### Workflow instance statuses

- `Pending` -- created, not yet claimed by the worker
- `Running` -- at least one step has been started
- `AwaitingRetry` -- a step failed but a retry is scheduled; the instance is waiting for the next attempt
- `Completed` -- all steps completed successfully
- `Failed` -- a step exhausted all retry attempts
- `Cancelled` -- manually cancelled before completion
- `Archived` -- manually archived after completion or failure; hidden from default list view

### Workflow step execution statuses

- `NotStarted` -- created but not yet eligible for claiming (e.g. waiting for a preceding step)
- `Pending` -- waiting to be claimed
- `Waiting` -- execution is paused, waiting for an external condition or delay
- `Running` -- currently claimed and executing
- `Completed` -- handler succeeded
- `Failed` -- handler failed (including timeouts and orphans)
- `Cancelled` -- step was cancelled as part of a workflow cancel operation

### WorkflowDefinitionStatus

- `Draft` -- definition is being authored, not yet runnable
- `Active` -- definition is live and can be triggered
- `Inactive` -- definition is paused; existing instances continue but new triggers are rejected
- `Archived` -- definition is retired and hidden from default views

### StepExecutionFailureClassification

- `TransientFailure` -- temporary issue (e.g. network timeout, 5xx response); eligible for retry
- `PermanentFailure` -- non-recoverable error (e.g. 4xx response, business logic rejection)
- `InvalidConfiguration` -- step configuration is malformed or references missing resources
- `InputResolutionFailure` -- step input could not be resolved (e.g. missing placeholder value, secret not found)

### BackoffStrategy

- `Fixed` -- retry delay is constant across attempts
- `Exponential` -- retry delay doubles (or increases by a configurable factor) with each attempt

## Event Types

| Event | Level | Trigger |
|-------|-------|---------|
| `WorkflowStarted` | instance | new instance created |
| `StepStarted` | step | handler began executing |
| `StepCompleted` | step | handler succeeded |
| `StepFailed` | step | handler threw an exception |
| `StepTimedOut` | step | handler exceeded `TimeoutSeconds` |
| `StepOrphaned` | step | lease expired without completion |
| `StepRetryScheduled` | step | new attempt queued after failure |
| `WorkflowCompleted` | instance | final step completed |
| `WorkflowFailed` | instance | step exhausted all attempts |
| `WorkflowRetried` | instance | manual retry from last failed step |
| `WorkflowReplayed` | instance | manual replay from step 1 |
| `WorkflowCancelled` | instance | manually cancelled |
| `WorkflowArchived` | instance | manually archived |

## Data Semantics

### Static metadata vs runtime state

Static metadata:

- `workflow_definitions`
- `workflow_definition_steps`
- `executable_workflow_definitions`
- `executable_trigger_definitions`
- `executable_step_definitions`
- `workflow_secrets`

Runtime state:

- `workflow_instances`
- `workflow_step_executions`
- `idempotency_records`
- `workflow_events`

Scheduling metadata (static config, runtime cursor):

- `recurring_workflow_schedules`

### Append-oriented execution history

A key design choice is that execution history is append-oriented:

- new attempts produce new step execution rows
- replay produces new step execution rows
- events are always additive

This makes the runtime easier to debug and audit.

### Tenant scope

Tenant scope applies to:

- users
- workflow instances
- idempotency records
- recurring workflow schedules

Workflow definitions and secrets are global in the current implementation.

That means:

- all tenants use the same registered workflow definitions
- secrets are not tenant-scoped
- tenant isolation is on execution, not on definition or secret metadata

## What To Look At When Debugging

If a workflow behaves unexpectedly, the most useful debugging sequence is:

1. inspect `workflow_instances` — current status and input
2. inspect all related `workflow_step_executions` ordered by `created_at` — attempt history, error, output
3. inspect all related `workflow_events` ordered by `created_at` — full timeline
4. inspect the corresponding `workflow_definition_steps` — intended retry policy, timeout, and config
5. for HTTP activity failures: the `output` column on the failed execution contains the HTTP response body

The ops console instance detail page surfaces steps 1–3 directly in the browser.
