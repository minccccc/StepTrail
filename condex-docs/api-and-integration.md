# API And Integration Overview

This document summarizes the HTTP surface exposed by `StepTrail.Api` as it exists today.

## API Style

The application exposes:

- a protected operations surface for UI pages and internal management APIs
- a public integration surface for trigger intake and health probing

The protected surface is built with:

- ASP.NET Core Minimal API endpoints
- Razor Pages
- cookie authentication

## Authentication

All operations APIs and all Razor Pages under the ops console require a valid session cookie.

Login endpoints:

- `GET /login`
- `POST /login`
- `POST /logout`

Credentials come from:

- `Ops:Username`
- `Ops:Password`

## Ops Console Routes

Workflow instance pages:

- `/ops/workflows` - instance list with status, key, trigger type, and date range filters; pagination
- `/ops/workflows/details?id={guid}` - instance detail with structured trail view and action buttons (retry, replay, cancel, archive)
- `/ops/workflows/create` - start a new workflow instance

Workflow definition pages:

- `/ops/definitions` - workflow definition list with source, status, and trigger info; edit, clone, and instances links
- `/ops/definitions/edit?id={guid}` - workflow definition editor (trigger config, step configs, retry policies, activate/deactivate)
- `/ops/definitions/new` - create a blank workflow definition
- `/ops/definitions/from-template` - create a workflow definition from a registry template
- `/ops/definitions/create?templateId={guid}` - clone an existing workflow definition

Template catalog:

- `/ops/templates` - template catalog with full configuration previews

The Razor Pages UI talks to the same host over loopback HTTP through `WorkflowApiClient`.

## Documentation Endpoints

- `GET /openapi/v1.json`
- `GET /scalar/v1`

## Public Endpoints

### Health Check

`GET /health`

Returns database connectivity status.

### Webhook Trigger Intake

`POST /webhooks/{routeKey}`

This endpoint is public and intended for external callers.

Current behavior:

- `routeKey` resolves an active webhook-triggered workflow definition
- request body must be valid JSON
- non-JSON or empty bodies are rejected
- `tenantId` query parameter is optional; if omitted, the default seeded tenant is used
- `X-External-Key` can be supplied for external correlation
- signature validation, input mapping, and idempotency extraction are applied when configured on the webhook trigger

## Protected Ops API

> All endpoints below require the session cookie used by the operations console.

## Template Catalog API

`GET /workflows`

Returns the registered template descriptor catalog, not persisted workflow definitions.

Current response shape includes:

- template key
- version
- name
- description
- ordered step descriptors

## Workflow Definition API

These endpoints operate on persisted executable workflow definitions.

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/workflow-definitions` | List all workflow definitions |
| `GET` | `/workflow-definitions/{id}` | Get definition detail |
| `POST` | `/workflow-definitions/blank` | Create blank definition |
| `POST` | `/workflow-definitions/from-descriptor` | Create from registry template |
| `POST` | `/workflow-definitions/clone` | Clone existing definition |
| `PUT` | `/workflow-definitions/{id}/trigger-type` | Change trigger type |
| `PUT` | `/workflow-definitions/{id}/trigger` | Update trigger configuration |
| `POST` | `/workflow-definitions/{id}/steps` | Add a step |
| `PUT` | `/workflow-definitions/{id}/steps/{stepKey}` | Update step configuration |
| `DELETE` | `/workflow-definitions/{id}/steps/{stepKey}` | Remove a step |
| `POST` | `/workflow-definitions/{id}/steps/{stepKey}/move-up` | Move step up |
| `POST` | `/workflow-definitions/{id}/steps/{stepKey}/move-down` | Move step down |
| `POST` | `/workflow-definitions/{id}/activate` | Activate definition |
| `POST` | `/workflow-definitions/{id}/deactivate` | Deactivate definition |
| `GET` | `/workflow-definitions/{key}/steps/{stepKey}/available-fields?version={version}` | Placeholder field discovery |

### Create From Template

`POST /workflow-definitions/from-descriptor`

```json
{
  "descriptorKey": "webhook-to-http-call",
  "descriptorVersion": 1,
  "name": "Forward customer webhooks",
  "key": "forward-customer-webhooks",
  "triggerType": "Webhook"
}
```

Current behavior:

- creates a new inactive workflow definition
- stores source template metadata
- seeds ordered steps from the template descriptor shape
- the trigger type is chosen at creation time

### Create Manual Workflow

`POST /workflow-definitions/blank`

```json
{
  "name": "Nightly sync",
  "key": "nightly-sync",
  "triggerType": "Schedule"
}
```

Current behavior:

- creates a new inactive workflow definition
- does not assign template origin metadata
- starts the manual authoring flow in the workflow editor

### Additional Trigger Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/manual-triggers/start` | Start workflow via manual trigger |

## Workflow Instance API

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/workflow-instances` | Start a new workflow instance |
| `GET` | `/workflow-instances` | List workflow instances |
| `GET` | `/workflow-instances/{id}` | Get one workflow instance |
| `GET` | `/workflow-instances/{id}/trail` | Structured trail with attempt history |
| `POST` | `/workflow-instances/{id}/retry` | Retry the failed execution point |
| `POST` | `/workflow-instances/{id}/replay` | Replay the instance |
| `POST` | `/workflow-instances/{id}/cancel` | Cancel the instance |
| `POST` | `/workflow-instances/{id}/archive` | Archive the instance |

### Start Workflow Instance

`POST /workflow-instances`

```json
{
  "workflowKey": "user-onboarding",
  "version": 1,
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "externalKey": "user-42",
  "idempotencyKey": "user-42-onboarding",
  "input": {
    "userId": 42,
    "email": "user@example.com"
  }
}
```

### Manual Trigger Start

`POST /manual-triggers/start`

This path starts an executable workflow definition through the Manual trigger model instead of the generic instance-start endpoint.

### Query API Notes

The list instances endpoint supports the following query parameters:

- `status` - filter by instance status
- `workflowKey` - filter by workflow key
- `triggerType` - filter by trigger type
- `createdFrom` - start of date range filter
- `createdTo` - end of date range filter
- `page` / `pageSize` - pagination

The read side currently supports:

- paged instance list with status, key, trigger type, and date range filters
- instance detail
- structured step trail with attempt history
- action eligibility flags for retry, replay, cancel, and archive

## Replay, Retry, Cancel, Archive

Current action semantics:

- `retry` creates another attempt at the failed execution point
- `replay` currently rebuilds execution from the beginning of the workflow path
- `cancel` prevents further progression where the runtime can safely observe cancellation
- `archive` hides the instance from the default list view

## Secrets Management

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/secrets` | List secret names and descriptions |
| `PUT` | `/secrets/{name}` | Create or update a secret |
| `DELETE` | `/secrets/{name}` | Delete a secret |

Secret values are never returned by the API.

Example:

```json
{
  "value": "sk-abc123",
  "description": "Third-party API key"
}
```

## Registered Workflows

The codebase currently registers two descriptor-backed templates:

- `user-onboarding`
- `webhook-to-http-call`

These are visible as templates in the catalog at `/ops/templates` (and via `GET /workflows`), where each template is displayed with full step configuration previews. Users can review a template preview, then click **Use Template** to create a workflow definition from it. The resulting definition is inactive and fully editable before activation.

## Default Tenant

For local development, the seeded default tenant is:

```text
00000000-0000-0000-0000-000000000001
```

Use it when testing:

- manual starts
- webhook starts without an explicit tenant
