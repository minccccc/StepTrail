# API And Integration Overview

This document summarizes the HTTP surface exposed by `StepTrail.Api`.

## API Style

The API is implemented as ASP.NET Core Minimal API endpoints.

It currently exposes:

- health and documentation endpoints
- workflow metadata endpoint
- command endpoints for start, retry, replay
- operational read endpoints for instance list, detail, and timeline

## Documentation Endpoints

When the API is running locally:

- OpenAPI document: `http://localhost:5000/openapi/v1.json`
- Scalar UI: `http://localhost:5000/scalar/v1`

## Main Endpoints

| Method | Path | Purpose |
| --- | --- | --- |
| `GET` | `/health` | Verify API to database connectivity |
| `GET` | `/workflows` | List registered workflow definitions |
| `POST` | `/workflow-instances` | Start a new workflow instance |
| `GET` | `/workflow-instances` | List workflow instances with filters and paging |
| `GET` | `/workflow-instances/{id}` | Get one workflow instance with step executions |
| `GET` | `/workflow-instances/{id}/timeline` | Get chronological workflow events |
| `POST` | `/workflow-instances/{id}/retry` | Retry from the most recent failed step |
| `POST` | `/workflow-instances/{id}/replay` | Replay the workflow from the first step |

## Start Workflow Request

`POST /workflow-instances`

Request body:

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

- `workflowKey`
  Required. Workflow descriptor key.
- `version`
  Optional. If omitted, the latest registered version is used.
- `tenantId`
  Required. Tenant for execution scope.
- `externalKey`
  Optional. Caller correlation value.
- `idempotencyKey`
  Optional. Used to return the existing instance if the same start request is replayed logically.
- `input`
  Optional. Arbitrary JSON payload stored with the instance and first step execution.

## Start Workflow Response

Example:

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

Meaning:

- `wasAlreadyStarted = false`
  The workflow was created by this request.
- `wasAlreadyStarted = true`
  The workflow already existed and was returned because of idempotency.

## List Workflow Instances

`GET /workflow-instances`

Supported query parameters:

- `tenantId`
- `workflowKey`
- `status`
- `page`
- `pageSize`

Example:

```text
/workflow-instances?tenantId=00000000-0000-0000-0000-000000000001&status=Failed&page=1&pageSize=20
```

Supported `status` values:

- `Pending`
- `Running`
- `Completed`
- `Failed`
- `Cancelled`

If `status` is invalid, the endpoint returns `400 Bad Request`.

## Get Workflow Detail

`GET /workflow-instances/{id}`

Returns:

- instance identity and metadata
- workflow key and version
- current workflow status
- stored input payload
- ordered step execution summaries

This endpoint is the best API-level view of execution history for a single workflow.

## Get Timeline

`GET /workflow-instances/{id}/timeline`

Returns events in chronological order.

Each timeline item may include:

- `eventType`
- `stepKey`
- `stepAttempt`
- `payload`
- `createdAt`

This is the operational event stream for one workflow instance.

## Retry And Replay

### Retry

`POST /workflow-instances/{id}/retry`

Behavior:

- valid only when the instance is `Failed`
- creates a new pending execution for the most recent failed step

### Replay

`POST /workflow-instances/{id}/replay`

Behavior:

- valid when the instance is `Failed` or `Completed`
- creates a new pending execution for the first step

Current note:

- replay currently restarts from the first step only

## Built-In Workflow

The current example workflow is:

- `user-onboarding`

Current steps:

1. `send-welcome-email`
2. `provision-account`
3. `notify-team`

## Default Tenant

For local development, the API seeds a stable default tenant:

```text
00000000-0000-0000-0000-000000000001
```

This is useful for:

- manual API testing
- UI prototyping
- local development flows
