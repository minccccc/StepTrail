# API And Integration Overview

This document summarizes the HTTP surface exposed by `StepTrail.Api`.

## API Style

The API is implemented as ASP.NET Core Minimal API endpoints plus Razor Pages.

It exposes two distinct surfaces:

- **protected ops surface** — requires cookie authentication; covers all management and read operations
- **public integration surface** — no authentication; covers webhook triggers and health probing

## Authentication

All ops API endpoints and the Razor Pages ops console require a valid session cookie.

Login:

- `GET /login` — login page
- `POST /login` — authenticates with username/password from config (`Ops:Username`, `Ops:Password`)
- `POST /logout` — clears the session cookie

Credentials are configured via `appsettings.json` or environment variable overrides (`Ops__Username`, `Ops__Password`). The committed defaults (`admin`/`admin`) should be overridden before any non-local deployment.

## Ops Console (Razor Pages)

The browser-based operations console is available at:

- `/ops/workflows` — instance list with status filter and archive toggle
- `/ops/workflows/details?id={guid}` — instance detail with step executions and timeline
- `/ops/workflows/create` — start a new workflow instance manually
- `/ops/templates` — template gallery
- `/ops/templates/setup?template=webhook-to-http-call` — guided setup wizard for the webhook-to-HTTP template

The console calls the ops REST API on loopback via `WorkflowApiClient`. The browser cookie is forwarded automatically by `ForwardAuthCookieHandler`.

## Documentation Endpoints (unauthenticated)

- `GET /openapi/v1.json` — OpenAPI document
- `GET /scalar/v1` — Scalar interactive UI

## Public Endpoints

### Health Check

`GET /health`

Returns database connectivity status:

```json
{ "status": "healthy", "database": "connected" }
```

Returns `503` with `"status": "unhealthy"` if the database is unreachable.

### Webhook Trigger

`POST /webhooks/{workflowKey}`

Starts a workflow instance. Intended for external event sources — no authentication required.

Request:

- URL path: `workflowKey` — the workflow descriptor key
- Query: `tenantId` (optional UUID) — defaults to the seeded default tenant if omitted
- Header: `X-Idempotency-Key` (optional) — deduplication key
- Header: `X-External-Key` (optional) — caller correlation value
- Body: any JSON payload — stored as workflow input (passed to the first step)

If the body is present but not valid JSON, the request still proceeds with null input.

Response: same shape as `POST /workflow-instances`.

## Ops API Endpoints

> All endpoints below require a valid session cookie.

### Workflow Definitions

`GET /workflows`

Returns all registered workflow definitions with their ordered step lists.

### Workflow Instances

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/workflow-instances` | Start a new instance |
| `GET` | `/workflow-instances` | List instances (paged, filterable) |
| `GET` | `/workflow-instances/{id}` | Get one instance with step summaries |
| `GET` | `/workflow-instances/{id}/timeline` | Get chronological event log |
| `POST` | `/workflow-instances/{id}/retry` | Retry from last failed step |
| `POST` | `/workflow-instances/{id}/replay` | Replay from step 1 |
| `POST` | `/workflow-instances/{id}/cancel` | Cancel a Pending or Running instance |
| `POST` | `/workflow-instances/{id}/archive` | Archive a Completed or Failed instance |

#### Start Workflow Request

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

Fields:

- `workflowKey` — required
- `version` — optional; latest version used if omitted
- `tenantId` — required
- `externalKey` — optional caller correlation value
- `idempotencyKey` — optional; returns the existing instance if already used by this tenant
- `input` — optional arbitrary JSON

#### Start Workflow Response

```json
{
  "id": "6d932f70-c51d-48ee-96c3-16dff0e80c72",
  "workflowKey": "user-onboarding",
  "version": 1,
  "tenantId": "00000000-0000-0000-0000-000000000001",
  "status": "Pending",
  "externalKey": "user-42",
  "idempotencyKey": "user-42-onboarding",
  "firstStepExecutionId": "0d3e56fa-3c24-4f1d-ae48-1c7ef4247d0d",
  "createdAt": "2026-04-11T10:00:00Z",
  "wasAlreadyStarted": false
}
```

`wasAlreadyStarted: true` means the instance already existed and was returned due to idempotency.

Returns `201 Created` for new instances, `200 OK` for idempotent returns.

#### List Instances

`GET /workflow-instances`

Query parameters:

- `tenantId` — filter by tenant
- `workflowKey` — filter by workflow key
- `status` — filter by status (`Pending`, `Running`, `Completed`, `Failed`, `Cancelled`, `Archived`)
- `includeArchived` — `true` to include archived instances (default `false`)
- `page` — 1-based page number (default `1`)
- `pageSize` — items per page, clamped 1–100 (default `20`)

Invalid `status` returns `400 Bad Request`.

#### Instance Detail

`GET /workflow-instances/{id}`

Returns instance metadata, current status, stored input, and ordered step execution summaries. The `output` field on a failed `HttpActivityHandler` step contains the HTTP response body — useful for diagnosing remote errors without replaying the step.

#### Timeline

`GET /workflow-instances/{id}/timeline`

Returns events in chronological order. Each item includes:

- `eventType`
- `stepKey`
- `stepAttempt`
- `payload`
- `createdAt`

#### Action Endpoints

All four action endpoints (`retry`, `replay`, `cancel`, `archive`) return:

- `200 OK` with a result summary on success
- `404 Not Found` if the instance does not exist
- `409 Conflict` if the instance is in a state that does not allow that action

### Secrets Management

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/secrets` | List all secret names and descriptions |
| `PUT` | `/secrets/{name}` | Create or update a named secret |
| `DELETE` | `/secrets/{name}` | Delete a named secret |

Secret values are **never** returned by any API endpoint.

#### Upsert Secret

`PUT /secrets/{name}`

```json
{
  "value": "sk-abc123",
  "description": "Third-party API key for notification service"
}
```

- `value` — required
- `description` — optional; retained from existing entry if omitted on update

Returns `201 Created` for new secrets, `200 OK` for updates.

Once stored, reference the secret in step configuration as `{{secrets.{name}}}`.

## Registered Workflows

Two workflows are currently registered in the system:

### `user-onboarding`

A sample workflow demonstrating code-first definition with three sequential handlers:

1. `send-welcome-email`
2. `provision-account`
3. `notify-team`

### `webhook-to-http-call`

A packaged template workflow with one step using the built-in `HttpActivityHandler`:

1. `http-call` — forwards the webhook payload to the URL stored in the `webhook-to-http-call-url` secret

Set up via the ops console at `/ops/templates/setup?template=webhook-to-http-call` or manually:

1. `PUT /secrets/webhook-to-http-call-url` — store the target URL
2. Trigger via `POST /webhooks/webhook-to-http-call` with a JSON body, or start manually from the ops console

## Default Tenant

For local development, the API seeds a stable default tenant:

```
00000000-0000-0000-0000-000000000001
```

Use this for:

- manual API testing
- webhook integration testing
- local development flows
