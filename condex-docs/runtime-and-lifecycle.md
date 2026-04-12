# Runtime And Lifecycle

This document explains how a workflow moves through the system.

## 1. Workflow Registration

Workflows are defined in code by inheriting from `WorkflowDescriptor`.

Each descriptor declares:

- `Key`
- `Version`
- `Name`
- `Description`
- ordered `Steps`

Each step declares:

- `StepKey`
- `StepType`
- `Order`
- `MaxAttempts`
- `RetryDelaySeconds`

At API startup:

- workflow descriptors are registered into the DI container
- `IWorkflowRegistry` exposes them in memory
- `WorkflowDefinitionSyncService` persists them into:
  - `workflow_definitions`
  - `workflow_definition_steps`

This creates a bridge between code-first authoring and database-driven runtime execution.

## 2. Starting A Workflow Instance

The main command endpoint is:

- `POST /workflow-instances`

The API receives:

- workflow key
- optional version
- tenant id
- optional external key
- optional idempotency key
- input payload

The `WorkflowInstanceService` then:

1. resolves the workflow descriptor from `IWorkflowRegistry`
2. verifies the tenant exists
3. checks whether the idempotency key was already used for that tenant
4. loads the persisted workflow definition and first step
5. creates:
   - one `workflow_instances` row
   - one initial `workflow_step_executions` row in `Pending`
   - one `workflow_events` row of type `WorkflowStarted`
   - one `idempotency_records` row if an idempotency key was supplied

At this moment, the workflow exists durably in the database but has not yet been processed by the worker.

## 3. Worker Polling And Claiming

The worker runs a continuous loop.

Its responsibility is:

- look for due `Pending` step executions
- claim exactly one execution at a time
- process it

Claiming is handled by `StepExecutionClaimer`.

The claim query:

- filters `Pending` executions
- filters rows where `scheduled_at <= now`
- orders by oldest due work
- uses `FOR UPDATE SKIP LOCKED`

That means multiple worker processes can poll concurrently without intentionally selecting the same row.

When a step is claimed, the worker updates the row to:

- `Status = Running`
- `LockedAt = now`
- `LockedBy = worker id`
- `StartedAt = now`

If the parent workflow instance is still `Pending`, it is moved to `Running`.

## 4. Step Execution

After claiming a step, `StepExecutionProcessor` takes over.

The processor:

1. loads the step definition
2. writes a `StepStarted` event
3. resolves the handler using keyed DI and the step's `StepType`
4. executes the handler
5. persists either success or failure

Handlers implement `IStepHandler`.

The current example workflow uses three handlers:

- `SendWelcomeEmailHandler`
- `ProvisionAccountHandler`
- `NotifyTeamHandler`

## 5. Success Path

If the handler succeeds:

- the current step execution is updated to `Completed`
- the handler output is stored on the execution row
- a `StepCompleted` event is written

Then the processor checks whether there is a next step.

### If a next step exists

A new `workflow_step_executions` row is inserted with:

- the next step id
- `Status = Pending`
- `Attempt = 1`
- `ScheduledAt = now`

The workflow instance stays `Running`.

### If there is no next step

The workflow instance is updated to:

- `Status = Completed`
- `CompletedAt = now`

And a `WorkflowCompleted` event is written.

## 6. Failure And Retry Path

If the handler throws:

- the current execution is marked `Failed`
- the error message is stored
- a `StepFailed` event is written

Then the processor compares:

- current `Attempt`
- step definition `MaxAttempts`

### If retries remain

A new pending execution row is inserted for the same step with:

- `Attempt = previous attempt + 1`
- `ScheduledAt = now + RetryDelaySeconds`

And a `StepRetryScheduled` event is written.

### If retries are exhausted

The workflow instance is updated to:

- `Status = Failed`

And a `WorkflowFailed` event is written.

## 7. Manual Recovery Operations

The API exposes two manual recovery commands.

### Retry

Endpoint:

- `POST /workflow-instances/{id}/retry`

Behavior:

- valid only for a `Failed` workflow instance
- finds the most recent failed step execution
- creates a fresh `Pending` execution for the same step
- resets `Attempt` to `1`
- moves the workflow instance back to `Running`
- writes a `WorkflowRetried` event

This preserves old history and appends a new branch of execution.

### Replay

Endpoint:

- `POST /workflow-instances/{id}/replay`

Behavior:

- valid for `Failed` or `Completed` instances
- creates a new `Pending` execution for the first step
- moves the workflow instance back to `Running`
- clears `CompletedAt`
- writes a `WorkflowReplayed` event

This also preserves previous history.

Important current behavior:

- replay currently restarts from the first step
- there is no request contract for replaying from an arbitrary step yet

## 8. Read-Side And Operational Visibility

The API exposes operational read endpoints for:

- list of workflow definitions
- paged list of workflow instances
- full detail for one instance
- timeline for one instance

The timeline is driven by `workflow_events`.

This gives operators a chronological record of what happened:

- when the workflow was started
- when a step began
- when it completed or failed
- when a retry was scheduled
- when the workflow completed or failed
- when a manual retry or replay occurred

## 9. Why The Design Works Well

This lifecycle is deliberately append-oriented and database-backed.

That gives the solution several strengths:

- durable coordination between API and worker
- traceability across retries and replay
- no hidden in-memory runtime state
- clear debugging path through instance, steps, and events

## 10. Current Behavioral Notes

These are important to know when reasoning about the current implementation:

- idempotent start is implemented at the service level and backed by `idempotency_records`
- retries create new execution rows rather than mutating the same execution repeatedly
- manual retry and replay also create new execution rows
- workflow history is preserved rather than overwritten
- workflow definitions are synced from code into the database at API startup
