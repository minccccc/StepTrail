# StepTrail Documentation

This folder is the code-aligned documentation set for the current StepTrail solution.

It describes the implementation that exists today, including the PBI-11 through PBI-20 additions:

- step timeouts and orphan recovery
- lease renewal and heartbeat-style lock extension
- delayed execution through `scheduled_at`
- recurring workflow dispatch
- webhook-triggered starts
- HTTP activity steps
- secret resolution
- operational alerts
- basic ops authentication
- the first packaged workflow template

These docs intentionally stay close to the code. Where behavior is still limited or incomplete, that is called out explicitly instead of being described as a finished capability.

## Recommended Reading Order

1. `architecture-overview.md`
   Best starting point. Explains the system shape, project responsibilities, and architectural style.
2. `runtime-and-lifecycle.md`
   Follows a workflow from registration through triggering, worker execution, retries, recurring dispatch, and operator actions.
3. `data-model.md`
   Describes the persistence model, including recurring schedules and workflow secrets.
4. `api-and-integration.md`
   Summarizes the public webhook surface, protected ops API, cookie auth, and packaged template flow.
5. `development-runbook.md`
   Covers local startup, configuration, operational checks, and current caveats.

## Quick Summary

StepTrail is a database-backed workflow engine built as a modular monolith.

At runtime it consists of:

- one ASP.NET Core API host
- one .NET worker host
- one PostgreSQL database

The API host is responsible for:

- protected operations UI and ops API
- public webhook trigger endpoint
- workflow registration and metadata sync
- workflow start / retry / replay / cancel / archive commands
- read-side instance and timeline queries
- secrets management endpoints

The worker host is responsible for:

- polling for due step executions
- claiming work safely from the database
- renewing execution leases while handlers run
- recovering orphaned executions whose locks expired
- executing step handlers
- scheduling retries and next steps
- dispatching recurring workflow schedules
- sending alerts for workflow failures and orphan recovery

The database is the shared source of truth for:

- workflow definitions and steps
- workflow instances and step executions
- idempotency records
- workflow events / timeline
- recurring workflow schedules
- workflow secrets

## Built-In Workflows

The solution currently ships with two code-first workflows:

- `user-onboarding`
- `webhook-to-http-call`

The second one is the first packaged template exposed in the operations UI.

## Important Current Caveats

- timeout handling is strongest when handlers cooperate with `CancellationToken`; a truly hung, non-cooperative handler can still remain `Running`
- secrets are stored in plaintext in the database
- the operations console uses basic app-configured credentials and should be overridden outside local development
- there are still no automated test projects in the solution

## Intended Audience

- developers onboarding to the solution
- reviewers trying to understand the current runtime behavior
- maintainers planning refactors without losing existing behavior
- future UI work that needs a clear backend mental model
