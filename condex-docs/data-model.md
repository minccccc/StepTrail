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
  |      +-- workflow_events (instance-level or step-level)
  |
  +-- idempotency_records

workflow_definitions
  |
  +-- workflow_definition_steps
  |
  +-- workflow_instances
```

## Core Tables

### `tenants`

Purpose:

- tenant partition for runtime data

Used by:

- users
- workflow instances
- idempotency records

Note:

- workflow definitions are not tenant-scoped in the current model

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

Important rules:

- `(workflow_definition_id, step_key)` is unique
- `(workflow_definition_id, order)` is unique

Interpretation:

- this table defines the static execution plan for a workflow version

### `workflow_instances`

Purpose:

- one concrete run of a workflow definition

Important fields:

- `tenant_id`
- `workflow_definition_id`
- `external_key`
- `idempotency_key`
- `status`
- `input`
- `created_at`
- `updated_at`
- `completed_at`

Interpretation:

- this is the parent row for execution state

### `workflow_step_executions`

Purpose:

- one concrete attempt of one workflow step

Important fields:

- `workflow_instance_id`
- `workflow_definition_step_id`
- `step_key`
- `status`
- `attempt`
- `input`
- `output`
- `error`
- `scheduled_at`
- `locked_at`
- `locked_by`
- `started_at`
- `completed_at`
- `created_at`
- `updated_at`

Important design point:

- retries and replay append new rows instead of erasing prior rows

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

- this is a lookup table from caller request identity to created workflow instance

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

## Status Enums

### Workflow instance statuses

- `Pending`
- `Running`
- `Completed`
- `Failed`
- `Cancelled`

### Workflow step execution statuses

- `Pending`
- `Running`
- `Completed`
- `Failed`
- `Cancelled`

## Event Types

The current event catalog is:

- `WorkflowStarted`
- `StepStarted`
- `StepCompleted`
- `StepFailed`
- `StepRetryScheduled`
- `WorkflowCompleted`
- `WorkflowFailed`
- `WorkflowRetried`
- `WorkflowReplayed`

## Data Semantics

### Static metadata vs runtime state

Static metadata:

- `workflow_definitions`
- `workflow_definition_steps`

Runtime state:

- `workflow_instances`
- `workflow_step_executions`
- `idempotency_records`
- `workflow_events`

### Append-oriented execution history

A key design choice in this schema is that execution history is append-oriented:

- new attempts produce new step execution rows
- replay produces new step execution rows
- events are always additive

This makes the runtime easier to debug and audit.

### Tenant scope

Tenant scope applies to:

- users
- workflow instances
- idempotency records

Workflow definitions are global in the current implementation.

That means:

- all tenants use the same registered workflow definitions
- tenant isolation is on execution, not on definition metadata

## What To Look At When Debugging

If a workflow behaves unexpectedly, the most useful debugging sequence is:

1. inspect `workflow_instances`
2. inspect all related `workflow_step_executions` ordered by `created_at`
3. inspect all related `workflow_events` ordered by `created_at`
4. inspect the corresponding `workflow_definition_steps`

This gives both:

- the intended static plan
- the actual runtime history
