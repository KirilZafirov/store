# Technical assignment requirements traceability

This matrix maps every requirement in `Tehnički zadatak - senior.docx` to the submitted evidence.

| Requirement | Status | Evidence |
|---|---|---|
| Global retail platform for millions of daily users | Complete | `docs/architecture.md`: system context, scaling and resilience, multi-region routing, partitioning and SLO assumptions |
| Web, mobile, marketplace and B2B channels | Complete | System-context diagram with channel BFFs and partner API; integration-adapter design |
| Scalability and high traffic | Complete | CDN/cache strategy, stateless horizontal scaling, backpressure, partitioning, read replicas, connection bounds and recovery targets |
| Secure transactions and data protection | Complete | OAuth 2.1/OIDC design, managed identities, encryption, Key Vault, PCI/tokenization boundary, capability-token implementation and `docs/threat-model.md` |
| Real-time data processing | Complete | Dedicated technology section, Azure Service Bus, outbox, idempotent consumers, event propagation and synchronous consistency boundaries |
| High-level architecture sketch | Complete | Mermaid system-context, container and checkout sequence diagrams in `docs/architecture.md` |
| Component communication | Complete | Versioned HTTPS for immediate decisions; Service Bus events for propagation/workflows; checkout saga sequence |
| Main technology selection | Complete | Dedicated technology-choice table with rationale and trade-offs |
| Scaling strategy | Complete | `docs/architecture.md#scaling-and-resilience` |
| Security and authentication | Complete | `docs/architecture.md#security-and-authentication` plus executable anonymous-cart authorization |
| Key components and responsibilities | Complete | Component responsibility/consistency table and explicit two-team ownership model |
| External integration such as tax authority | Complete | Anti-corruption adapters, synchronous/legal decision rule, queues, idempotency, replay and audit design |
| Monitoring, alerting and health checks | Complete | OpenTelemetry, Prometheus endpoint, SLO/alert table, structured logs, `/health/live` and database-aware `/health/ready` |
| Code-delivery plan | Complete | Phased roadmap, expand/migrate/contract releases, canary promotion and rollback strategy |
| CI/CD | Complete | `.github/workflows/ci.yml`, locked restore, builds, tests, audits, immutable container publishing and documented promotion model |
| Branching strategy | Complete | Trunk-based development, short-lived feature branches, protected `main`, conventional commits and review policy |
| Minimal cart Web API | Complete | Create/get cart and add/update/remove/clear-item endpoints with OpenAPI and RFC 9457 errors |
| Database included | Complete | EF Core PostgreSQL model, migration, Docker Compose database, integration tests and deployed Neon database |
| Selected production requirements implemented | Complete | Optimistic concurrency, durable idempotency, rate limiting, token authorization, Redis degradation, OpenTelemetry and health checks |
| Online repository | Complete | <https://github.com/KirilZafirov/store> |
| Incremental commits | Complete | Separate domain, API/persistence, tests, UI, architecture, deployment and production-fix commits |
| README with startup instructions | Complete | Root `README.md`: Docker and local startup, migrations, verification, API examples, deployment and troubleshooting |

## Additional evidence beyond the minimum

- React/TypeScript storefront deployed at <https://atlas-cart-store.vercel.app>.
- Containerized .NET API deployed at <https://atlas-cart-api-kiril.onrender.com>.
- PostgreSQL persistence deployed on Neon in Frankfurt.
- Production browser smoke test verified create cart, add item, change quantity and subtotal recalculation with no console errors.
- Domain tests, container-backed PostgreSQL API tests, React component tests, linting and dependency audits are included.

## Deliberate scope boundaries

The assignment asks for one minimal cart API, not live payment, tax-authority, inventory or identity integrations. Those integrations are therefore designed at architecture level through explicit ports, adapters, events and failure policies rather than represented by misleading placeholder production connections.
