# ADR 0002: PostgreSQL is the cart source of truth

**Status:** Accepted

## Decision

Persist carts, items, versions and idempotency records in PostgreSQL. Store monetary amounts as `numeric(19,2)` plus a three-letter currency. Use optimistic concurrency per cart.

## Consequences

Transactions make mutations and retry protection straightforward, and operational knowledge is common. A single write leader has regional/throughput limits; measured growth may require partitioning or migration of the active-cart access pattern to a globally distributed key-value store.
