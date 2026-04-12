# Workflow Authoring Guide

This guide walks through creating a new workflow from scratch: defining the workflow descriptor, implementing step handlers, and registering everything.

The built-in `UserOnboardingWorkflow` (3 steps) in `src/StepTrail.Api/Workflows/` is the reference example for everything described here.

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
using StepTrail.Worker.Handlers; // only needed for nameof() references

public sealed class OrderFulfillmentWorkflow : WorkflowDescriptor
{
    public override string Key => "order-fulfillment";
    public override int Version => 1;
    public override string Name => "Order Fulfillment";
    public override string? Description => "Reserves inventory, charges payment, and ships the order.";

    public override IReadOnlyList<WorkflowStepDescriptor> Steps =>
    [
        new("reserve-inventory", nameof(ReserveInventoryHandler), order: 1),
        new("charge-payment",    nameof(ChargePaymentHandler),    order: 2, maxAttempts: 5, retryDelaySeconds: 60),
        new("ship-order",        nameof(ShipOrderHandler),        order: 3),
    ];
}
```

### WorkflowStepDescriptor Parameters

| Parameter | Type | Required | Default | Notes |
|-----------|------|----------|---------|-------|
| `stepKey` | `string` | yes | — | Unique key within this workflow. Used in queries and logs. |
| `stepType` | `string` | yes | — | Must match the handler's keyed DI registration. Use `nameof(YourHandler)`. |
| `order` | `int` | yes | — | 1-based execution order. Must be unique within the workflow. |
| `maxAttempts` | `int` | no | `3` | How many times this step will be attempted before the workflow is marked Failed. |
| `retryDelaySeconds` | `int` | no | `30` | Seconds to wait before scheduling the next attempt after a failure. |

### Versioning

- `Key` + `Version` must be globally unique across all registered workflows.
- To change a workflow, create a new class with the same `Key` and a higher `Version`.
- Old versions remain in the database and continue running for existing instances.
- `FindLatest` (used when no version is specified in a start request) always returns the highest registered version.

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
    {
        _logger = logger;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        // context.Input is the JSON string output from the previous step
        // (or the workflow's initial Input for the first step)
        _logger.LogInformation(
            "Reserving inventory for instance {InstanceId}, input: {Input}",
            context.WorkflowInstanceId, context.Input);

        // ... your business logic here ...

        // Return output that the next step will receive as Input
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
| `Input` | `string?` | JSON string from the previous step's output, or the workflow's initial input for step 1 |

### StepResult

```csharp
// Success with output passed to next step
return StepResult.Success("""{ "reservationId": "abc-123" }""");

// Success with no output (next step receives null input)
return StepResult.Success();
```

### Failure Handling

**Do not return a failure result — throw an exception.** Any unhandled exception is caught by `StepExecutionProcessor`, stored as the error message, and triggers the retry/failure logic.

```csharp
// This triggers a retry (or marks the workflow failed if retries exhausted)
throw new InvalidOperationException("Payment gateway timeout");
```

### Dependency Injection

Handlers are resolved from the DI container (scoped), so you can inject any registered service:

```csharp
public sealed class ChargePaymentHandler : IStepHandler
{
    private readonly IPaymentGateway _gateway;
    private readonly ILogger<ChargePaymentHandler> _logger;

    public ChargePaymentHandler(IPaymentGateway gateway, ILogger<ChargePaymentHandler> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    public async Task<StepResult> ExecuteAsync(StepContext context, CancellationToken ct)
    {
        var result = await _gateway.ChargeAsync(context.Input, ct);
        return StepResult.Success(result.ToJson());
    }
}
```

---

## Step 3 — Register Everything

### Register the workflow descriptor in the API

In `src/StepTrail.Api/Program.cs`, add:

```csharp
builder.Services.AddWorkflow<OrderFulfillmentWorkflow>();
```

This registers the descriptor with the `IWorkflowRegistry`. At startup, `WorkflowDefinitionSyncService` will sync it to the database.

### Register the handlers in the Worker

In `src/StepTrail.Worker/Program.cs`, add a keyed registration for each handler:

```csharp
builder.Services.AddKeyedScoped<IStepHandler, ReserveInventoryHandler>(nameof(ReserveInventoryHandler));
builder.Services.AddKeyedScoped<IStepHandler, ChargePaymentHandler>(nameof(ChargePaymentHandler));
builder.Services.AddKeyedScoped<IStepHandler, ShipOrderHandler>(nameof(ShipOrderHandler));
```

The key **must match** the `stepType` you passed to `WorkflowStepDescriptor`. Using `nameof(YourHandler)` in both places guarantees this.

---

## Step 4 — Start an Instance

Restart the API (it will sync the new workflow definition to the DB), then:

```http
POST /workflow-instances
Content-Type: application/json

{
  "workflowKey": "order-fulfillment",
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "externalKey": "order-9871",
  "idempotencyKey": "fulfill-order-9871",
  "input": { "orderId": 9871, "items": [{ "sku": "ABC", "qty": 2 }] }
}
```

The worker will pick up the first step within one poll interval (default 5 seconds).

---

## Data Flow Between Steps

Output from one step becomes input to the next. All values are JSON strings.

```
POST /workflow-instances  →  input: { "orderId": 9871 }
                                        ↓
ReserveInventoryHandler   ←  input: { "orderId": 9871 }
                          →  output: { "reservationId": "abc-123", "orderId": 9871 }
                                        ↓
ChargePaymentHandler      ←  input: { "reservationId": "abc-123", "orderId": 9871 }
                          →  output: { "chargeId": "ch_xyz", "orderId": 9871 }
                                        ↓
ShipOrderHandler          ←  input: { "chargeId": "ch_xyz", "orderId": 9871 }
                          →  output: { "trackingNumber": "1Z999AA..." }
```

**Convention:** each step should pass through any data the downstream steps will need. The pattern is to spread the existing context into each output so nothing gets lost.

---

## Monitoring a Running Instance

```bash
# Check current status
GET /workflow-instances/{id}

# See each state transition with timestamps
GET /workflow-instances/{id}/timeline
```

The timeline shows every event: `WorkflowStarted`, `StepStarted`, `StepCompleted`, `StepFailed`, `StepRetryScheduled`, `WorkflowCompleted`, `WorkflowFailed`.

---

## Recovery Operations

If a workflow instance ends up in `Failed` state:

```bash
# Retry from the last failed step (resets attempt counter to 1)
POST /workflow-instances/{id}/retry

# Replay from the very first step
POST /workflow-instances/{id}/replay
```

`replay` also works on `Completed` instances — useful for re-running a workflow that completed but produced bad output, or for testing.

---

## Checklist for a New Workflow

- [ ] Create `YourWorkflow.cs` in `src/StepTrail.Api/Workflows/` extending `WorkflowDescriptor`
- [ ] Set a unique `Key`, `Version`, and ordered `Steps`
- [ ] For each step, create a handler in `src/StepTrail.Worker/Handlers/` implementing `IStepHandler`
- [ ] Register `AddWorkflow<YourWorkflow>()` in `src/StepTrail.Api/Program.cs`
- [ ] Register each handler with `AddKeyedScoped<IStepHandler, YourHandler>(nameof(YourHandler))` in `src/StepTrail.Worker/Program.cs`
- [ ] Restart the API (syncs definition to DB) and the Worker
- [ ] Verify the workflow appears in `GET /workflows`
- [ ] Start a test instance via `POST /workflow-instances`
