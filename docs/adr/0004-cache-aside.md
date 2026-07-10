# ADR 0004: Remove Redis from the executable cart slice

**Status:** Superseded by PR 5 decision

## Decision

Remove Redis and the cart cache abstraction from the submitted executable slice. PostgreSQL remains the only runtime source of truth for cart reads, writes, authorization, optimistic concurrency and durable idempotency.

Keep Redis in the target platform architecture only for use cases where it can remove meaningful load or coordinate distributed runtime behavior, such as read projections, anonymous/session state owned by a BFF, distributed rate limiting, short-lived partner throttling state, or carefully designed hot catalog/pricing projections.

## Context

The previous cache-aside implementation still loaded the cart from PostgreSQL before checking Redis so that it could authorize the capability token and compare versions safely. That means the cache did not avoid the source-of-truth database read on the main cart path. It also added invalidation, connection management, package, Compose and deployment configuration surface without improving correctness or materially reducing latency for this vertical slice.

## Consequences

Fresh clones no longer require Redis configuration or a Redis container. Cart behavior is simpler to explain and operate: PostgreSQL either serves the authoritative cart or readiness reports the dependency failure.

The trade-off is that the executable slice does not demonstrate distributed caching. That is deliberate. A future Redis-backed optimization should start with a concrete access pattern and threat model, then prove it actually avoids database work without caching raw capability tokens or weakening object-level authorization.
