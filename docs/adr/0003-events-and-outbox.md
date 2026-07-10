# ADR 0003: Events for cross-domain state propagation

**Status:** Accepted for target architecture; not required by the isolated demo

## Decision

Use Azure Service Bus and a transactional outbox for durable cross-domain events. Consumers are idempotent, version tolerant, observable, and use dead-letter handling. Keep request/response HTTP for immediate decisions.

## Consequences

Services can absorb bursts and release independently. Teams must design for duplicates, eventual consistency, message evolution, replay and operational ownership. The demo does not add a broker merely to claim event-driven behavior.
