# .NET Backend Agent Guide

## Scope

These instructions apply to production .NET code under `src/`. The solution is a pragmatic Clean Architecture cart service and a credible extraction point for a future retail platform.

## Architecture boundaries

- `Cart.Domain` owns aggregates, value objects, invariants, and domain errors. It must not depend on Application, Infrastructure, ASP.NET Core, EF Core, or transport contracts.
- `Cart.Application` owns use cases, ports, commands/queries, validation, and transport-neutral DTOs. It may depend on Domain, never Infrastructure or API.
- `Cart.Infrastructure` implements persistence, cache, security, and external adapters. Keep provider-specific behavior behind Application abstractions.
- `Cart.Api` is the composition and HTTP boundary: endpoints, authentication hooks, rate limiting, exception mapping, OpenAPI, telemetry, and health checks.
- Preserve inward dependency direction. Do not bypass the Application layer from an endpoint to EF Core.

## Domain and API rules

- Mutate cart state only through the `ShoppingCart` aggregate so quantity, currency, totals, version, and timestamps remain consistent.
- Use `decimal` for money and always carry an ISO currency code. Never use binary floating point for monetary values.
- Maintain strong consistency within one cart and optimistic concurrency across requests. Map stale writes to `409 Conflict`.
- Mutating endpoints must support durable idempotency. Commit the idempotency record in the same PostgreSQL transaction as the mutation.
- Store only a cryptographic hash of the opaque anonymous cart token and compare it in constant time. Never log tokens, connection strings, or credentials.
- Return RFC 9457 Problem Details consistently for validation, authorization, missing resources, conflicts, and unexpected errors.
- Treat client-supplied product names and prices as demo snapshots. Production pricing comes from Pricing and is revalidated during checkout.
- PostgreSQL remains the source of truth. Redis is cache-aside and optional; Redis failure may reduce performance but must not compromise correctness.
- Keep liveness process-only and readiness dependency-aware. Preserve structured logs, OpenTelemetry, and Prometheus metrics when adding endpoints.

## Database and migrations

- Use EF Core migrations for schema changes; never rely on `EnsureCreated` in application code.
- Generate migrations with:

  ```bash
  dotnet ef migrations add ChangeName --project src/Cart.Infrastructure --startup-project src/Cart.Api
  ```

- Make migrations compatible with controlled production rollout. Avoid destructive schema changes without an expand/migrate/contract plan.
- Do not enable automatic migrations for horizontally scaled production replicas; use a release job. `ApplyMigrations=true` is limited to local/demo deployment.

## Verification

Before completing a backend change, run:

```bash
dotnet build RetailPlatform.slnx
dotnet test tests/Cart.Domain.Tests
dotnet test tests/Cart.Api.Tests
```

The API tests require Docker because they use a real disposable PostgreSQL instance. Treat warnings as errors and preserve nullable reference type correctness.

## Change boundaries

- Prefer extending the existing modular service over creating superficial services. Extract a service only when ownership, scaling, deployment, or data-boundary evidence justifies it.
- When changing public behavior, update tests, OpenAPI, frontend contracts, README examples, and architecture/ADR documentation where applicable.
- Do not add real payment, tax authority, identity-provider, or marketplace credentials. Model those integrations through ports, adapters, and test doubles.

