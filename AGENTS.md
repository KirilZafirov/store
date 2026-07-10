# Repository Agent Guide

## Purpose and scope

These instructions apply to the whole repository. More specific `AGENTS.md` files override or extend them within their directories:

- `src/AGENTS.md` — production .NET backend rules.
- `tests/AGENTS.md` — .NET backend test rules.
- `frontend/AGENTS.md` — React and TypeScript frontend rules.

This repository is an interview-ready vertical slice for a global retail platform. Optimize for a working, explainable, production-minded Cart capability rather than broad but superficial feature coverage.

## Start every task with context

- Read the nearest applicable `AGENTS.md` file before editing.
- Review `README.md`, `docs/architecture.md`, and the relevant ADR when a change affects architecture, deployment, data, security, or public contracts.
- Inspect the working tree before editing. Preserve user changes and do not rewrite unrelated files.
- Keep code, tests, documentation, and deployment configuration consistent with one another.

## Repository workflow

- Use trunk-based development with short-lived `feature/*` branches when branches are needed and protected `main` for reviewed work.
- Make a commit after each meaningful, independently understandable section. Do not collapse the exercise into one final commit.
- Use conventional commit messages such as `feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`, and `ci:`.
- Keep commits focused. Do not mix unrelated cleanup with functional changes.
- Never commit secrets, provider credentials, local environment files, `.vercel`, build output, or editor/OS artifacts.
- Do not use destructive Git operations or overwrite existing work unless explicitly authorized.

## Architecture principles

- PostgreSQL is authoritative for the executable cart slice. Redis belongs only in the target architecture when a measured projection, session, distributed-rate-limit, or hot-data pattern justifies it.
- Use synchronous HTTP for immediate client operations and asynchronous events for cross-domain workflows in the target architecture.
- Avoid unsafe database/message dual writes; use a transactional outbox and idempotent consumers when event publishing is implemented.
- Keep cart contents strongly consistent within a cart. Shopping-time inventory views may be eventually consistent; checkout performs authoritative reservation.
- Coordinate checkout, order, inventory, payment, and tax through a saga rather than distributed transactions.
- Store money as decimal values with an ISO currency code. Floating-point monetary arithmetic is prohibited.
- Prefer cloud-portable application code even though Azure is the production reference architecture.

## Security and reliability baseline

- Apply least privilege, input validation, object-level authorization, rate limiting, idempotency, and optimistic concurrency at the appropriate boundaries.
- Never log access tokens, authorization headers, secrets, connection strings, or sensitive customer data.
- Use RFC 9457 Problem Details for API errors without leaking internal implementation details.
- Preserve OpenTelemetry traces and metrics, structured logging, liveness/readiness checks, and actionable failure states.
- Treat external systems as unreliable: set timeouts, retry only safe operations, apply backoff, and plan circuit breaking and dead-letter handling where appropriate.

## Required verification

Run checks proportional to the change. Before completing a cross-stack or release-related change, run the full suite:

```bash
dotnet build RetailPlatform.slnx
dotnet test tests/Cart.Domain.Tests
dotnet test tests/Cart.Api.Tests
cd frontend
npm ci
npm run lint
npm test
npm run build
```

API integration tests require Docker. For deployment changes, also verify the container build, `/health/ready`, Swagger, and the production cart flow.

Do not claim a check passed unless it ran successfully. Clearly distinguish implementation/build verification from runtime or cloud verification when an external dependency is unavailable.

## Contract and documentation discipline

- A public API change must update backend contracts, validation, OpenAPI, frontend types/client, automated tests, and relevant examples together.
- An architectural decision or important trade-off must be recorded in `docs/architecture.md` or a focused ADR.
- Keep `docs/requirements-traceability.md` accurate when assignment coverage changes.
- Keep `README.md` startup, migration, testing, troubleshooting, and deployment instructions executable from a fresh clone.

## Scope guardrails

- Do not introduce new frameworks, services, cloud dependencies, or abstractions without a concrete requirement and an explainable trade-off.
- Do not implement fake production integrations or commit real provider credentials. Use ports, adapters, test doubles, and documented extension points.
- Preserve the intentional modular-monolith delivery approach while documenting how bounded contexts can be extracted when scale or ownership requires it.
