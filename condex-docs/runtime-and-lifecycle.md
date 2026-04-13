# Runtime And Lifecycle

This document explains how a workflow moves through the current StepTrail system.

## 1. Workflow Registration

Workflows are defined in code by inheriting from `WorkflowDescriptor`.

Each descriptor declares:

- `Key`
- `Version`
- `Name`
- `Description`
- optional `RecurrenceIntervalSeconds`
- ordered `Steps`

Each step declares:

- `StepKey`
- `StepType`
- `Order`
- `MaxAttempts`
- `RetryDelaySeconds`
- optional `TimeoutSeconds`
- optional handler-specific `Config`

At API startup:

- workflow descriptors are registered into DI
- `IWorkflowRegistry` exposes them in memory
- `WorkflowDefinitionSyncService` persists them into:
  - `workflow_definitions`
  - `workflow_definition_steps`
- if a descriptor declares `RecurrenceIntervalSeconds`, the API also creates a `recurring_workflow_schedules` row for that definition the first time it is synced

This creates a bridge between code-first authoring and database-driven runtime execution.

## 2. Triggering A Workflow

There are currently two ways to start a workflow instance.

### Protected operator start

Endpoint:

- `POST /workflow-instances`

This is part of the authenticated ops API.

The request accepts:

- workflow key
- optional version
- tenant id
- optional external key
- optional idempotency key
- optional input payload

### Public webhook start

Endpoint:

- `POST /webhooks/{workflowKey}`

This endpoint is intentionally unauthenticated.

It uses:

- `X-Idempotency-Key` header
- `X-External-Key` header
- optional `tenantId` query parameter
- JSON request body as workflow input

If `tenantId` is omitted, the default seeded tenant is used.

Important current behavior:

- if the body is malformed JSON, the request still proceeds and the workflow is started with `null` input

## 3. Creating The Instance And First Step

Both trigger paths delegate to the same `WorkflowInstanceService`.

The service:

1. resolves the workflow descriptor
2. verifies the tenant exists
3. checks idempotency if an idempotency key was supplied
4. loads the persisted workflow definition and first step
5. creates:
   - one `workflow_instances` row
   - one initial `workflow_step_executions` row in `Pending`
   - one `workflow_events` row of type `WorkflowStarted`
   - one `idempotency_records` row if needed

At this moment, the workflow exists durably in the database but has not yet been processed by the worker.

## 4. Worker Loop

The worker runs a continuous loop. Each iteration does three things in order:

1. recover orphaned executions
2. dispatch due recurring workflow schedules
3. claim and process one due step execution

That keeps recovery, scheduled work, and normal work in one simple polling model.

## 5. Claiming A Step Execution

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
- `LockExpiresAt = now + lock window`
- `StartedAt = now`

If the parent workflow instance is still `Pending`, it is moved to `Running`.

## 6. Executing A Step

After claiming a step, `StepExecutionProcessor` takes over.

The processor:

1. loads the step definition
2. writes a `StepStarted` event
3. resolves the handler using keyed DI and the step's `StepType`
4. builds a `StepContext` containing:
   - workflow instance id
   - step execution id
   - step key
   - execution input
   - step config JSON
5. applies an optional timeout token when `TimeoutSeconds` is configured
6. starts `StepLeaseRenewer` so `lock_expires_at` keeps moving forward while the handler is running
7. persists either success or failure

Current built-in handlers are:

- `SendWelcomeEmailHandler`
- `ProvisionAccountHandler`
- `NotifyTeamHandler`
- `HttpActivityHandler`

## 7. Success Path

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
- `Input = previous step output`
- `ScheduledAt = now`

The workflow instance stays `Running`.

### If there is no next step

The workflow instance is updated to:

- `Status = Completed`
- `CompletedAt = now`

And a `WorkflowCompleted` event is written.

## 8. Failure, Timeout, And Retry Path

If the handler throws:

- the current execution is marked `Failed`
- the error message is stored
- a failure event is written:
  - `StepFailed` for normal exceptions
  - `StepTimedOut` when the timeout token cancels the handler

Then the worker compares:

- current `Attempt`
- step definition `MaxAttempts`

### If retries remain

A new pending execution row is inserted for the same step with:

- `Attempt = previous attempt + 1`
- `Input = previous execution input`
- `ScheduledAt = now + RetryDelaySeconds`

And a `StepRetryScheduled` event is written.

### If retries are exhausted

The workflow instance is updated to:

- `Status = Failed`

And a `WorkflowFailed` event is written.

If the workflow failed permanently, the worker also sends alerts through the configured alert channels.

## 9. Orphan Recovery

Before claiming new work, the worker scans for `Running` executions whose `lock_expires_at <= now`.

Those are treated as orphaned:

- the worker assumes the previous worker died or disappeared
- the old execution is marked failed with a `StepOrphaned` event
- normal retry policy is applied

This is the crash-recovery path for work that was claimed but never finished.

Important current caveat:

- timeout handling is still partly cooperative
- a handler that never returns and ignores cancellation can keep renewing its lease and remain `Running`
- so orphan recovery is strong for crashed workers, but not a full cure for every possible hung handler

## 10. Delayed Execution And Scheduling

The worker only claims executions where:

- `Status = Pending`
- `ScheduledAt <= now`

That means `scheduled_at` is the gating mechanism for delayed work.

Today it is used by:

- retries
- normal next-step scheduling
- recurring workflow dispatch

There is no separate scheduler service.

## 11. Recurring Workflow Dispatch

Recurring schedules are stored in `recurring_workflow_schedules`.

During each loop, `RecurringWorkflowDispatcher`:

1. finds enabled rows where `next_run_at <= now`
2. locks them with `FOR UPDATE SKIP LOCKED`
3. creates:
   - a new `workflow_instances` row
   - a new first-step `workflow_step_executions` row
   - a `WorkflowStarted` event
4. updates:
   - `last_run_at`
   - `next_run_at = now + interval`

Important current note:

- the runtime supports recurrence, but no built-in workflow currently declares a recurrence interval

## 12. HTTP Activity And Secrets

`HttpActivityHandler` enables outbound HTTP calls from workflows.

Its config can define:

- `Url`
- `Method`
- optional `Headers`
- optional `Body`

Before sending the request, `SecretResolver` replaces placeholders like:

- `{{secrets.some-name}}`

The values come from `workflow_secrets`.

Important properties of the current implementation:

- secrets are stored in plaintext
- secrets are not returned by the API, only their names and descriptions are exposed
- on non-2xx responses, the HTTP handler throws a typed exception
- the worker persists the response status/body on the failed execution for debugging

## 13. Manual Operator Actions

The ops API exposes four manual actions.

### Retry

Endpoint:

- `POST /workflow-instances/{id}/retry`

Behavior:

- valid only for a `Failed` workflow instance
- finds the most recent failed execution
- creates a fresh `Pending` execution for the same step
- resets `Attempt` to `1`
- moves the workflow instance back to `Running`
- writes a `WorkflowRetried` event

### Replay

Endpoint:

- `POST /workflow-instances/{id}/replay`

Behavior:

- valid for `Failed` or `Completed` instances
- creates a new `Pending` execution for the first step
- moves the workflow instance back to `Running`
- clears `CompletedAt`
- writes a `WorkflowReplayed` event

Important current behavior:

- replay currently restarts from step 1 only

### Cancel

Endpoint:

- `POST /workflow-instances/{id}/cancel`

Behavior:

- marks the instance `Cancelled`
- cancels any `Pending` step executions
- writes a `WorkflowCancelled` event

Important current caveat:

- a step already `Running` is not cooperatively interrupted by this API call

### Archive

Endpoint:

- `POST /workflow-instances/{id}/archive`

Behavior:

- hides the instance from the default list view
- cancels any `Pending` step executions
- writes a `WorkflowArchived` event

## 14. Read-Side Visibility

The ops API exposes read endpoints for:

- workflow definitions
- paged workflow instance list
- instance detail
- timeline

The timeline is driven by `workflow_events`.

That gives operators a chronological record of what happened:

- when the workflow was started
- when a step began
- when it completed, failed, timed out, or was orphaned
- when a retry was scheduled
- when the workflow completed, failed, was retried, replayed, cancelled, or archived

## 15. Why This Design Works Well

This lifecycle remains deliberately append-oriented and database-backed.

That gives the solution several strengths:

- durable coordination between API and worker
- traceability across retries and replay
- no hidden in-memory runtime state
- clear debugging path through instance, steps, events, schedules, and secrets

## 16. Current Behavioral Notes

These are important to keep in mind when reasoning about the current implementation:

- idempotent start is implemented at the service level and backed by `idempotency_records`
- retries, replay, and recurring dispatch all create new execution rows instead of mutating one row forever
- workflow definitions are synced from code into the database at API startup
- the ops UI is authenticated, but the webhook trigger is intentionally public
- webhook malformed JSON currently results in `null` input instead of a `400`
