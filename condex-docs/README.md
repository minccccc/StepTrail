# StepTrail Documentation

This folder is the code-aligned documentation set for the current StepTrail solution.

It describes the implementation that exists today, including:

- template catalog and workflow-definition authoring
- manual workflow creation and template-based creation
- executable trigger and step configuration
- retry, replay, cancel, and archive operations
- workflow instance trail and operations UI
- webhook, API, manual, and schedule trigger paths
- delay and retry waiting behavior
- secrets and placeholder resolution
- retry policy model with configurable backoff strategies
- failure classification for step executions
- AwaitingRetry workflow instance status
- structured trail view with attempt history
- workflow definition CRUD (create, edit, activate/deactivate, clone)
- three-level UX model: Templates, Workflow Definitions, Instances
- all trigger types: Webhook, Manual, API, Schedule
- all step types: HttpRequest, SendWebhook, Transform, Conditional, Delay
- source template tracking on definitions
- comprehensive test suite with Testcontainers

These docs intentionally stay close to the current codebase. Where behavior is still limited or intentionally transitional, that is called out explicitly.

## Recommended Reading Order

1. `architecture-overview.md`
   Best starting point for the current runtime shape and project responsibilities.
2. `api-and-integration.md`
   Best entry point for routes, authoring endpoints, trigger endpoints, and the operations console surface.
3. `runtime-and-lifecycle.md`
   Follows how workflows are started, executed, retried, waited, and recovered.
4. `development-runbook.md`
   Covers local startup, useful URLs, testing, and current caveats.

## Quick Summary

StepTrail is a database-backed workflow engine with three top-level product concepts:

- **Templates** - code-registered workflow blueprints shown in the Templates catalog
- **Workflows** - persisted executable workflow definitions owned by the user/system
- **Workflow Instances** - concrete executions of workflows

At runtime it consists of:

- one ASP.NET Core API host
- one .NET worker host
- one PostgreSQL database

The API host is responsible for:

- the protected operations UI and ops API
- template catalog and workflow authoring flows
- template catalog UI at /ops/templates with configuration previews
- public trigger endpoints
- workflow definition CRUD and activation
- workflow activation and lifecycle commands
- read-side queries for workflows and workflow instances
- structured trail queries with attempt history
- secrets management

The worker host is responsible for:

- polling for due executions
- claiming and processing step executions
- retries and waiting semantics
- failure classification for step execution outcomes
- retry policy resolution with configurable backoff
- recurring dispatch
- orphan recovery

The database is the source of truth for:

- workflow definitions
- workflow instances
- step executions and attempt history
- events and trail data
- schedules
- secrets

## Current Built-In Templates

The solution currently ships with two code-registered templates:

- `user-onboarding`
- `webhook-to-http-call`

These appear in `/ops/templates` with full configuration previews and can be turned into editable workflow definitions through the **Use Template** flow. Users can also create workflow definitions manually from scratch without starting from a template.

## Important Current Caveats

- template descriptors currently provide the ordered step shape; trigger type is chosen when the user instantiates the template
- manual workflow creation is available, but workflow definitions still require at least one persisted step definition
- replay behavior is still evolving and should be treated as an active area of refinement
- secrets are stored in the database and should be handled accordingly for non-local deployments
- the operations console uses app-configured credentials and must be hardened outside local development

## Intended Audience

- developers onboarding to the codebase
- maintainers updating the workflow engine and authoring model
- reviewers validating runtime and operations behavior
- future UI work that needs an accurate mental model of Templates, Workflows, and Workflow Instances
