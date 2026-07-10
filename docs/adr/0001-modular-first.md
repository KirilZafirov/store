# ADR 0001: Deliver a modular vertical slice before service extraction

**Status:** Accepted

## Decision

Use Domain/Application/Infrastructure/API boundaries inside the submitted Cart service and define the wider platform as domain services. Do not manufacture empty deployables for the exercise. Extract domains when team ownership, scaling, reliability isolation or release cadence provides evidence.

## Consequences

Delivery and local debugging remain simple and transactional behavior is visible. Discipline is required to prevent cross-module coupling. Extraction later requires contract and data migration work, but the ports and database-ownership rules make that work explicit.
