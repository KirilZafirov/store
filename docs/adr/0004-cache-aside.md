# ADR 0004: Redis is an optional cache, not cart authority

**Status:** Accepted

## Decision

Use cache-aside reads keyed by cart ID, validate cached versions against the database entity, invalidate after writes, and tolerate Redis connection/operation failure. Keep a short TTL.

## Consequences

Cache outages reduce performance rather than correctness or availability. Database reads still occur for authorization and version validation, so this implementation favors safety over maximum read offload. A production optimization can cache authorization metadata carefully after threat and revocation analysis.
