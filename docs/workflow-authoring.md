# Workflow Authoring Guide

This guide walks through creating a new workflow from scratch: defining the workflow descriptor, implementing step handlers, and registering everything.

The built-in examples are:
- `UserOnboardingWorkflow` (3 steps: send welcome email → provision account → notify team) in `src/StepTrail.Api/Workflows/`
- `WebhookToHttpCallWorkflow` (1 step: forward webhook payload to an HTTP endpoint) — the packaged template workflow

---

## Overview

A workflow in StepTrail consists of two parts:

1. **A workflow descriptor** — a C# class that declares the workflow's identity and its ordered list of steps. Lives in `StepTrail.Api` (or wherever you start instances from).
2. **Step handlers** — one C# class per step, implementing `IStepHandler`. Live in `StepTrail.Worker` (where steps are executed).

---

## Step 1 — Define the Workflow Descriptor

Create a class in `src/StepTrail.Api/Workflows/` that extends `WorkflowDescriptor`:

```csharp
// src/StepTrail.Api/Workflows/OrderFulfillmentWorkflow.cs
using StepTrail.Shared.Workflows;

public sealed class OrderFulfillmentWorkflow : WorkflowDescriptor
{
    public override string Key => "order-fulfillment";
    public override int Version => 1;
    public override string Name => "Order Fulfillment";
    public override string? Description => "Reserves inventory, charges payment, and ships the order.";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new("reserve-inventory", nameof(ReserveInventoryHandler), order: 1),
        new("charge-payment",    nameof(ChargePaymentHandler),    order: 2,
            maxAttempts: 5, retryDelaySeconds: 60),
        new("ship-order",        nameof(ShipOrderHandler),        order: 3,
            timeoutSeconds: 120),
    ];
}
```

### WorkflowStepDescriptor Parameters

| Parameter | Type | Required | Default | Notes |
|-----------|------|----------|---------|-------|
| `stepKey` | `string` | yes | — | Unique key within this workflow. Used in queries and logs. |
| `stepType` | `string` | yes | — | Must match the handler's keyed DI registration. Use `nameof(YourHandler)`. |
| `order` | `int` | yes | — | 1-based execution order. Must be unique within the workflow. |
| `maxAttempts` | `int` | no | `3` | How many times this step is attempted before the workflow is marked Failed. |
| `retryDelaySeconds` | `int` | no | `30` | Seconds to wait before scheduling the next attempt after a failure. |
| `timeoutSeconds` | `int?` | no | `null` | If set, the handler is cancelled after this many seconds. Null = no timeout. |
| `config` | `object?` | no | `null` | Handler-specific configuration. Serialised to JSON and stored in the DB. |

### Versioning

- `Key` + `Version` must be globally unique.
- To change a workflow's steps or config, create a new class with the same `Key` and a higher `Version`.
- Old versions remain in the database and continue running for existing instances.
- `FindLatest` always returns the highest registered version.

### Recurring Workflows

Override `RecurrenceIntervalSeconds` to have the worker automatically start a new instance on a fixed schedule:

```csharp
public override int? RecurrenceIntervalSeconds => 3600; // every hour
```

`WorkflowDefinitionSyncService` creates a `recurring_workflow_schedules` row on first startup. `RecurringWorkflowDispatcher` in the worker fires new instances when `next_run_at` is due and advances the schedule.

---

## Step 2 — Implement Step Handlers

Create one class per step in `src/StepTrail.Worker/Handlers/`, implementing `IStepHandler` from `StepTrail.Shared`:

```csharp
// src/StepTrail.Worker/Handlers/ReserveInventoryHandler.cs
using StepTrail.Shared.Workflows;

public sealed class ReserveInventoryHandler : IStepHandler
{
    private readonly ILogger<ReserveInventoryHandler> _logger;

    public ReserveInventoryHandler(ILogger<ReserveInventoryHandler> logger)
        => _logger = logger;

    public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        // context.Input  — JSON string output from the previous step (or workflow input for step 1)
        // context.Config — JSON string from WorkflowStepDescriptor config (null if not set)
        _logger.LogInformation(
            "Reserving inventory for instance {InstanceId}", context.WorkflowInstanceId);

        // ... your business logic ...

        return StepResult.Success("""{ "reservationId": "abc-123" }""");
    }
}
```

### StepContext

| Property | Type | Description |
|----------|------|-------------|
| `WorkflowInstanceId` | `Guid` | The running workflow instance ID |
| `StepExecutionId` | `Guid` | This step execution's ID |
| `StepKey` | `string` | The step key (e.g. `"reserve-inventory"`) |
| `Input` | `string?` | JSON from the previous step's output, or the workflow's initial input for step 1 |
| `Config` | `string?` | Handler-specific JSON config from the step definition (null if not set) |

### StepResult

```csharp
// Success with output passed to the next step
return StepResult.Success("""{ "reservationId": "abc-123" }""");

// Success with no output
return StepResult.Success();
```

### Failure Handling

**Do not return a failure result — throw an exception.** Any unhandled exception is caught by `StepExecutionProcessor`, stored as the error, and triggers the retry/failure logic.

```csharp
// Triggers retry (or marks the workflow Failed if retries are exhausted)
throw new InvalidOperationException("Payment gateway timeout");
```

### Timeouts

If `TimeoutSeconds` is configured on the step, the `CancellationToken` passed to `ExecuteAsync` is cancelled after that many seconds. Honor it by using `ct` in all async calls:

```csharp
await _httpClient.PostAsync(url, content, ct); // will throw OperationCanceledException on timeout
```

A step that exceeds its timeout is recorded with event type `StepTimedOut` and treated as a failure (retry/fail policy applies normally).

### Dependency Injection

Handlers are resolved from the DI container (scoped), so you can inject any registered service:

```csharp
public sealed class ChargePaymentHandler : IStepHandler
{
    private readonly IPaymentGateway _gateway;

    public ChargePaymentHandler(IPaymentGateway gateway) => _gateway = gateway;

    public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        var result = await _gateway.ChargeAsync(context.Input, ct);
        return StepResult.Success(result.ToJson());
    }
}
```

---

## Built-In Handler: HttpActivityHandler

`HttpActivityHandler` is a built-in step handler that makes outbound HTTP calls. Use it without writing any custom handler code — just configure the step with a `config` object.

```csharp
new WorkflowStepDescriptor(
    stepKey:           "notify-webhook",
    stepType:          "HttpActivityHandler",
    order:             1,
    maxAttempts:       3,
    retryDelaySeconds: 30,
    timeoutSeconds:    30,
    config: new
    {
        Url     = "https://api.example.com/ingest",
        Method  = "POST",                        // default POST
        Headers = new { Authorization = "Bearer {{secrets.my-api-key}}" },
        Body    = null                           // null = forward step input as body
    })
```

Config fields:

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `Url` | `string` | required | Target URL. Supports `{{secrets.name}}` placeholders. |
| `Method` | `string` | `"POST"` | HTTP method. |
| `Headers` | `object?` | `null` | Key/value pairs added to the request. Values support `{{secrets.name}}`. |
| `Body` | `string?` | `null` | Static request body. If null, the step's `Input` JSON is used as the body. |

On a non-2xx response, the handler throws and the retry policy applies. The HTTP status code and response body are persisted as the step's `Output` even on failure — inspect them in the ops console timeline.

---

## Secrets

Store sensitive values (API keys, tokens, URLs) in the `workflow_secrets` table and reference them from step configs using `{{secrets.name}}` placeholders.

**Setting a secret** — via the ops console (`/ops/templates` setup page, or direct API):

```http
PUT /secrets/my-api-key
Content-Type: application/json

{ "value": "sk-...", "description": "Third-party API key" }
```

**Referencing a secret** in a step config:

```csharp
config: new
{
    Url    = "https://api.example.com/endpoint",
    Headers = new { Authorization = "Bearer {{secrets.my-api-key}}" }
}
```

`SecretResolver` in the worker batch-loads all referenced secrets before executing the step. Secret values are never stored in step config, execution rows, or returned by any API endpoint.

---

## Step 3 — Register Everything

### Register the workflow descriptor in the API

In `src/StepTrail.Api/Program.cs`:

```csharp
builder.Services.AddWorkflow<OrderFulfillmentWorkflow>();
```

`WorkflowDefinitionSyncService` syncs it to the database on next startup.

### Register the handlers in the Worker

In `src/StepTrail.Worker/Program.cs`, add a keyed registration for each handler:

```csharp
builder.Services.AddKeyedScoped<IStepHandler, ReserveInventoryHandler>(nameof(ReserveInventoryHandler));
builder.Services.AddKeyedScoped<IStepHandler, ChargePaymentHandler>(nameof(ChargePaymentHandler));
builder.Services.AddKeyedScoped<IStepHandler, ShipOrderHandler>(nameof(ShipOrderHandler));
```

The key **must match** the `stepType` in the descriptor. Using `nameof(YourHandler)` in both places guarantees this.

`HttpActivityHandler` is already registered — no action needed for steps that use it.

---

## Step 4 — Start an Instance

Via the REST API:

```http
POST /workflow-instances
Content-Type: application/json

{
  "workflowKey": "order-fulfillment",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "externalKey": "order-9871",
  "idempotencyKey": "fulfill-order-9871",
  "input": { "orderId": 9871 }
}
```

Via the webhook endpoint (useful for external event sources):

```http
POST /webhooks/order-fulfillment
X-External-Key: order-9871
X-Idempotency-Key: fulfill-order-9871
Content-Type: application/json

{ "orderId": 9871 }
```

Via the ops console: navigate to `/ops/workflows` → **+ New Instance**.

---

## Data Flow Between Steps

Output from one step becomes input to the next. All values are JSON strings.

```
Workflow input:            { "orderId": 9871 }
                                    ↓
ReserveInventoryHandler ← input:  { "orderId": 9871 }
                        → output: { "reservationId": "abc-123", "orderId": 9871 }
                                    ↓
ChargePaymentHandler    ← input:  { "reservationId": "abc-123", "orderId": 9871 }
                        → output: { "chargeId": "ch_xyz", "orderId": 9871 }
                                    ↓
ShipOrderHandler        ← input:  { "chargeId": "ch_xyz", "orderId": 9871 }
                        → output: { "trackingNumber": "1Z999AA..." }
```

**Convention:** pass through any data downstream steps will need. There is no separate "workflow context" — the output chain is the context.

---

## Recovery Operations

```http
# Retry from the last failed step (resets attempt counter to 1)
POST /workflow-instances/{id}/retry

# Replay from step 1
POST /workflow-instances/{id}/replay

# Cancel a Pending or Running instance
POST /workflow-instances/{id}/cancel

# Archive a Completed or Failed instance (hides it from default list view)
POST /workflow-instances/{id}/archive
```

All operations are also available from the ops console detail page.

---

## Checklist for a New Workflow

- [ ] Create `YourWorkflow.cs` in `src/StepTrail.Api/Workflows/` extending `WorkflowDescriptor`
- [ ] Set a unique `Key`, `Version`, and ordered `Steps`
- [ ] For each custom step, create a handler in `src/StepTrail.Worker/Handlers/` implementing `IStepHandler`
- [ ] Steps using `HttpActivityHandler` need no custom handler — configure via `config:`
- [ ] Reference secrets using `{{secrets.name}}` in config strings; set values via `PUT /secrets/{name}`
- [ ] Register `AddWorkflow<YourWorkflow>()` in `src/StepTrail.Api/Program.cs`
- [ ] Register each custom handler with `AddKeyedScoped<IStepHandler, YourHandler>(nameof(YourHandler))` in `src/StepTrail.Worker/Program.cs`
- [ ] Restart the API (syncs definition to DB) and the Worker
- [ ] Verify the workflow appears in `GET /workflows`
- [ ] Start a test instance via `POST /workflow-instances` or the ops console
