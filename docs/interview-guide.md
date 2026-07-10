# Interview discussion guide

Use this as a concise walkthrough during the technical interview. The strongest framing is: this repository proves one high-quality vertical slice and documents how it grows into a global retail platform without pretending every integration is already built.

## Opening narrative

Atlas Retail Cart is a production-minded cart capability for a larger retail platform. The implementation focuses on correctness under retries, concurrency and invalid input. The architecture document then shows how the same boundaries expand into catalog, pricing, inventory, checkout, orders, payments, fulfilment, notifications and external integrations.

The deliberate choice was depth over pretend breadth: one reliable Cart API and a working React UI are more credible than many empty microservices.

## What is implemented

- ASP.NET Core Cart API with Clean Architecture-style Domain/Application/Infrastructure/API boundaries.
- PostgreSQL source of truth with EF Core migrations.
- Anonymous cart capability tokens stored only as hashes.
- Create/get cart, add item, update quantity, remove item and clear cart.
- Decimal money plus ISO currency; mixed currencies and invalid values are rejected.
- Optimistic concurrency with `409 Conflict`.
- Durable request-aware idempotency for mutating requests.
- RFC 9457 Problem Details with stable error codes.
- Rate limiting, forwarded-header hardening, health checks, OpenTelemetry and Prometheus metrics.
- React/TypeScript cart UI with retry-safe mutations and accessible states.
- Docker Compose, CI, browser e2e tests, container scanning and SBOM generation.
- Public demo deployed through Neon, Render and Vercel.

## What is designed but not implemented

- Live OIDC/OAuth identity provider and authenticated cart merging.
- Catalog, pricing, inventory, checkout, orders, payments and fulfilment services.
- Azure Service Bus, transactional outbox and idempotent consumers.
- Real tax-authority, payment-provider, marketplace or B2B partner adapters.
- Multi-region active-active deployment, autoscaling rules and production alert policies.
- Image signing.

This is intentional. The exercise requires a minimal cart API plus architecture. Fake integrations would create false confidence and credentials/secrets risk.

## Key trade-offs to explain

### Modular vertical slice before microservices

Pros: simpler local development, fewer distributed failure modes, transactional correctness is easier to demonstrate, and boundaries are still visible in code.

Cons: extraction later requires discipline, contracts and data migration. The design mitigates that by keeping domain ownership, ports and database boundaries explicit.

### PostgreSQL as cart source of truth

Pros: ACID updates, optimistic concurrency, durable idempotency, decimal money and strong consistency within one cart.

Cons: a single regional write path eventually needs partitioning, home-region routing or a different globally distributed active-cart store.

### Durable idempotency

Network retries are normal in browsers and mobile apps. Mutations accept an idempotency key and store the operation, request fingerprint and successful response in the same transaction as the cart update. Identical retries replay the original result; key reuse with a different request returns `409`.

Cart creation is excluded because replaying creation would require storing the raw capability token. The implementation stores only token hashes.

### Event-driven target architecture

In the target architecture, Service Bus is used for cross-domain propagation and long-running workflows, not for immediate decisions. Cart updates, price validation at checkout, stock reservation and payment authorization stay synchronous where the user needs an authoritative answer.

### Redis removed from executable slice

The previous cache still needed PostgreSQL first for authorization and version checks, so it did not reduce the critical database read. Removing it made the executable slice more honest. Redis remains useful for carefully designed projections, sessions or distributed rate limiting.

## Requirement coverage talking points

- **Scalability:** stateless API, PostgreSQL indexing/timeouts, rate limits and containerization are implemented; CDN, BFFs, partitioning, read replicas, Service Bus depth scaling and multi-region DR are target architecture.
- **Secure transactions/data protection:** implemented hashed capability tokens, object authorization, validation, concurrency and idempotency; target architecture adds OIDC, Key Vault, managed identities and PCI tokenization boundaries.
- **Real-time processing:** target design uses Service Bus events and outbox; executable slice exposes metrics and immediate HTTP behavior but does not run a broker.
- **External services:** tax/payment/marketplace integrations are designed as anti-corruption adapters with idempotency, throttling, audit and replay; no fake credentials are included.
- **Monitoring/alerting:** implemented health endpoints, Prometheus metrics and structured logs; target architecture defines dashboards, SLOs, alerts and runbooks.

## Demo flow

1. Open the storefront.
2. Add the same product twice and show quantity aggregation.
3. Change quantity, remove, add another item and clear the cart.
4. Open Swagger and show the API surface.
5. Show `/health/live`, `/health/ready` and `/metrics`.
6. Mention CI runs backend tests, frontend tests, browser smoke, container builds/scans and SBOM generation.

## Good questions to invite

- When would you extract Cart into an independent service?
- How would authenticated cart merge work?
- How would checkout coordinate inventory, payment, tax and orders without distributed transactions?
- What data would move through Service Bus and what must stay synchronous?
- When would Redis become worth adding back?
- What would be required before going from the public demo to production?
