# .NET Test Agent Guide

## Scope

These instructions apply to .NET tests under `tests/` and complement the production guidance in `src/AGENTS.md`.

## Test strategy

- Put pure aggregate and value-object behavior in `Cart.Domain.Tests`; keep these tests fast and infrastructure-free.
- Put HTTP contracts, persistence, authorization, idempotency, concurrency, health, and Problem Details behavior in `Cart.Api.Tests`.
- Exercise PostgreSQL-specific behavior against the existing containerized PostgreSQL fixture rather than substituting an EF in-memory provider.
- Assert observable behavior and contracts. Avoid tests that merely duplicate implementation details.
- Use deterministic identifiers and data. Do not depend on execution order, shared mutable state, external cloud services, or production credentials.
- Every defect fix should include a regression test that fails without the fix.

## Required coverage for cart changes

- Aggregate invariants, quantities, currency consistency, totals, removal, and clearing.
- Successful API behavior plus malformed input, missing carts, wrong cart tokens, and RFC 9457 responses.
- Stale-version updates returning `409 Conflict`.
- Retried idempotent mutations applying exactly once.
- One cart token being unable to read or mutate another cart.
- Readiness behavior when PostgreSQL is available and unavailable where practical.

## Verification

Run the focused project while developing, then the complete solution checks before completion:

```bash
dotnet test tests/Cart.Domain.Tests
dotnet test tests/Cart.Api.Tests
dotnet build RetailPlatform.slnx
```

Docker must be running for `Cart.Api.Tests`.
