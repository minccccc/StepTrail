# StepTrail Documentation

This folder is the code-aligned documentation set for the current StepTrail solution.

It is meant to answer three practical questions:

1. What is this system, architecturally?
2. How does the workflow engine behave end to end?
3. How do we run it, integrate with it, and reason about future changes?

These documents describe the implementation that exists in the solution today. They intentionally stay close to the code and avoid describing aspirational architecture that has not been implemented yet.

## Recommended Reading Order

1. `architecture-overview.md`
   Best starting point. Explains the runtime shape, project responsibilities, and architectural style.
2. `runtime-and-lifecycle.md`
   Follows a workflow from registration through execution, retry, replay, and read APIs.
3. `data-model.md`
   Describes the persistence model and how the main entities relate to each other.
4. `api-and-integration.md`
   Summarizes the HTTP surface and the key request/response contracts.
5. `development-runbook.md`
   Covers local development, startup sequence, configuration, and current limitations.

## Quick Summary

StepTrail is a database-backed workflow engine built as a modular monolith.

At runtime it consists of:

- one ASP.NET Core API host
- one .NET worker host
- one PostgreSQL database

The API host is responsible for:

- startup concerns
- workflow registration
- metadata sync
- creating workflow instances
- manual retry and replay
- operational read endpoints

The worker host is responsible for:

- polling for due step executions
- safely claiming work from the database
- executing step handlers
- persisting step outcomes
- scheduling retries or next steps

The database is the shared source of truth for:

- workflow definitions
- workflow instances
- step executions
- idempotency records
- workflow events / timeline

## Intended Audience

- developers onboarding to the solution
- reviewers trying to understand the runtime behavior
- future UI work that needs a clear backend mental model
- maintainers planning refactors without losing current behavior
