# Workflow Authoring Guide

This guide describes the two supported approaches for authoring workflows in StepTrail:

- **Code-first**: workflow descriptors (templates) registered in code, synced to the system at startup
- **UI-based**: create and edit workflow definitions through the ops console at `/ops/definitions` (blank, from template, or clone)

Both approaches produce the same runtime artifact: an executable workflow definition that can be activated, triggered, and monitored.

## Top-Level Product Model

StepTrail has three distinct concepts arranged in a hierarchy:

- **Template** -- a predefined, code-registered workflow blueprint shown in the Templates catalog. Read-only; sourced from `IWorkflowRegistry`.
- **Workflow Definition** -- a persisted, user-owned workflow definition that can be edited, activated, or deactivated.
- **Workflow Instance** -- one concrete execution of a workflow definition.

The relationships are:

- `Template -> Workflow Definition -> Workflow Instance`
- `Manual / Clone creation -> Workflow Definition -> Workflow Instance`

Templates are never edited directly in the UI. A user either creates a workflow definition from a template, creates one from scratch, or clones an existing definition.

## Authoring Surfaces

The Razor Pages authoring and operations UI is split into three top-level areas:

- `/ops/templates` -- template catalog with full configuration preview
- `/ops/definitions` -- workflow definitions list
- `/ops/workflows` -- workflow instances list

Within those areas, the main authoring pages are:

- `/ops/definitions/from-template?descriptorKey=...&descriptorVersion=...` -- create a workflow from a template
- `/ops/definitions/new` -- create a blank workflow
- `/ops/definitions/edit?id={guid}` -- edit one workflow definition
- `/ops/templates` -- browse registered templates and preview their configuration

## Trigger Types

Each workflow definition has exactly one trigger. The three supported trigger types are:

| Type | Description |
|------|-------------|
| **Webhook** | Receives external HTTP payloads with signature validation and input mapping. |
| **Manual** | Triggered from the ops console. |
| **Schedule** | Recurring execution via a fixed interval or cron expression. |

The trigger section in the editor renders a type-specific configuration form for the currently selected trigger type.

## Step Types

Workflow definitions contain an ordered list of steps. The five supported step types are:

| Type | Description |
|------|-------------|
| **HttpRequest** | Outbound HTTP calls with configurable URL, method, headers, body, and timeout. |
| **SendWebhook** | Similar to HttpRequest but optimized for webhook delivery. |
| **Transform** | Maps and transforms data between steps using input mappings. |
| **Conditional** | Evaluates conditions on step output (SourcePath, Operator, ExpectedValue, FalseOutcome). |
| **Delay** | Pauses workflow execution for a specified duration or until a target time. |

Steps are rendered in order in the editor. Each step shows its key, type, order, an edit form for type-specific configuration, move up/down actions, and a remove action (when the workflow has more than one step).

## Code-First Authoring (Templates)

Templates are implemented as code-registered workflow descriptors.

To add a new template:

1. Create a `WorkflowDescriptor` in `src/StepTrail.Api/Workflows/`
2. Register it in `src/StepTrail.Api/Program.cs` with `AddWorkflow<T>()`
3. Restart the API so `WorkflowDefinitionSyncService` can sync metadata

The template catalog reads registered descriptors from `GET /workflows`.

Current descriptor shape:

- key
- version
- name
- description
- ordered step descriptors

Important current limitation:

- Descriptor metadata does not currently include trigger-shape metadata.
- Because of that, trigger type is chosen when the user clicks **Use Template**.

### Retry on Descriptors vs. RetryPolicy

Workflow step descriptors support `maxAttempts` and `retryDelaySeconds` fields. These are still used for code-first definitions. When a workflow definition is created from a descriptor, these values are converted into a `RetryPolicy` object, which is the model used at runtime.

The `RetryPolicy` supports:

| Field | Description |
|-------|-------------|
| MaxAttempts | Maximum number of execution attempts. |
| InitialDelaySeconds | Delay before the first retry. |
| BackoffStrategy | `Fixed` or `Exponential`. |
| MaxDelaySeconds | Upper bound on delay when using exponential backoff. |
| RetryOnTimeout | Whether to retry when a step times out. |

## UI-Based Workflow Authoring

The ops console provides a full visual authoring experience at `/ops/definitions`.

### Creating a Workflow Definition

There are three ways to create a new workflow definition:

1. **From Template** -- browse `/ops/templates`, pick a template, and click **Use Template**. The template's step configuration is used as the starting shape.
2. **+ New Workflow** -- click the button at `/ops/definitions` to create a blank definition.
3. **Clone** -- clone an existing workflow definition from the definitions list.

In all three cases the new definition starts in **Inactive** status.

### Editing a Workflow Definition

The editor at `/ops/definitions/edit?id={guid}` allows full configuration:

- **Trigger**: select and configure one of the three trigger types (Webhook, Manual, Schedule). Each trigger type renders a type-specific configuration form.
- **Steps**: add, remove, and reorder steps. Each step type (HttpRequest, SendWebhook, Transform, Conditional, Delay) has its own configuration form.
- **Retry policy**: each step supports a per-step retry policy override with MaxAttempts, InitialDelaySeconds, BackoffStrategy (Fixed or Exponential), MaxDelaySeconds, and RetryOnTimeout. If no custom policy is set, the default is 3 attempts with a 10-second fixed delay.
- **Source indicator**: the editor shows `Manual` or `From template: {key}` so you know the definition's origin. Template-derived definitions remember their source template key and version.

Editing is only allowed when the workflow status is `Inactive`.

### Activation Lifecycle

Workflow definitions move through these states:

- `Inactive`
- `Active`

Current authoring flow:

- Newly created workflows start as `Inactive`
- Activation runs backend validation
- Invalid activation returns explicit validation errors
- Deactivation moves an active workflow back to `Inactive`

## Failure and Retry Behavior

### Failure Classification

When a step execution fails, the system classifies the failure into one of four categories:

| Classification | Behavior |
|----------------|----------|
| **TransientFailure** | Retried according to the step's retry policy. |
| **PermanentFailure** | Skips retries regardless of attempts remaining. |
| **InvalidConfiguration** | Skips retries regardless of attempts remaining. |
| **InputResolutionFailure** | Skips retries regardless of attempts remaining. |

### Retry Scheduling

When a transient failure occurs and the step has attempts remaining, the system:

1. Computes the next retry delay based on the step's retry policy and backoff strategy.
2. Creates a new pending step execution scheduled for the computed time.
3. Sets the workflow instance status to **AwaitingRetry**.

The `StepExecutionClaimer` background service picks up `Pending` and `AwaitingRetry` step executions when their scheduled time arrives.

## Recovery Operations

When a workflow instance fails or needs intervention, the following operations are available:

| Operation | Description |
|-----------|-------------|
| **Retry** | Resumes from the last failed step (not step 1). Creates a new step execution for the failed step and moves the instance back to `Running`. |
| **Replay** | Restarts from step 1. Re-materializes all step executions and includes a version safety check. Moves the instance back to `Running`. |
| **Cancel** | Terminates the workflow instance. |
| **Archive** | Moves a terminal instance to archived state. Note: instances in `AwaitingRetry` status cannot be archived. |

## Placeholder Assistance

Placeholder-capable fields in the editor use the available-fields read surface.

Current behavior:

- The UI requests suggestions per workflow key, workflow version, and step key.
- Suggestions are scoped to fields available up to the current step.
- Suggestions can be inserted into URL, header, body, transform, conditional, and delay expression fields.

The backing endpoint is:

- `GET /workflow-definitions/{key}/steps/{stepKey}/available-fields?version={version}`

## Related APIs

Authoring-related endpoints:

- `GET /workflows` -- registered template descriptors
- `GET /workflow-definitions` -- persisted workflow definitions
- `POST /workflow-definitions/from-descriptor` -- create a workflow from a template descriptor
- `POST /workflow-definitions/blank` -- create a blank workflow definition
- `POST /workflow-definitions/clone` -- clone an existing workflow definition
- `GET /workflow-definitions/{id}` -- get one workflow definition
- `PUT /workflow-definitions/{id}/trigger-type` -- replace the current trigger type
- `PUT /workflow-definitions/{id}/trigger` -- save trigger configuration
- `POST /workflow-definitions/{id}/steps` -- add a step
- `PUT /workflow-definitions/{id}/steps/{stepKey}` -- save step configuration
- `DELETE /workflow-definitions/{id}/steps/{stepKey}` -- remove a step
- `POST /workflow-definitions/{id}/steps/{stepKey}/move-up` -- move a step earlier
- `POST /workflow-definitions/{id}/steps/{stepKey}/move-down` -- move a step later
- `POST /workflow-definitions/{id}/activate` -- activate a workflow definition
- `POST /workflow-definitions/{id}/deactivate` -- deactivate a workflow definition

## Recommended Operator Flow

For day-to-day use in the current UI:

1. Browse templates in `/ops/templates` or start manually in `/ops/definitions/new`
2. Edit the workflow in `/ops/definitions/edit?id={guid}`
3. Configure the trigger
4. Configure and order the steps
5. Adjust per-step retry behavior if needed
6. Activate the workflow
7. Monitor executions in `/ops/workflows`
