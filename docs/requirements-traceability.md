# Technical assignment requirements traceability

This matrix maps the assignment requirements to repository evidence and separates implemented behavior from target-platform design. “Implemented” means it runs in this repository or the public demo. “Designed” means it is covered by architecture, ADRs or documented extension points without pretending a live external integration exists.

| Requirement | Coverage | Evidence |
|---|---|---|
| Global retail platform for millions of daily users | Designed | `docs/architecture.md`: system context, scaling and resilience, home-region cart routing, partitioning, load-shedding, SLO assumptions and recovery targets |
| Web, mobile, marketplace and B2B channels | Designed | System-context diagram with channel BFFs, partner API and anti-corruption adapters |
| Scalability and high traffic | Implemented + designed | Implemented: stateless API container, bounded database pool/timeouts, rate limiting and Dockerized deployment. Designed: CDN, edge throttling, horizontal scaling, partitioning, read replicas, backpressure and multi-region DR |
| Secure transactions and data protection | Implemented + designed | Implemented: hashed opaque cart tokens, object-level cart authorization, optimistic concurrency, durable idempotency, validation, Problem Details and no raw-token persistence. Designed: OAuth/OIDC, managed identities, Key Vault, encryption, PCI/provider-tokenization boundary |
| Real-time data processing | Designed | Azure Service Bus, transactional outbox, idempotent consumers and event propagation are target-state architecture. The executable cart slice intentionally does not include a broker |
| High-level architecture sketch | Designed | Mermaid system-context, container and checkout sequence diagrams in `docs/architecture.md` |
| Component communication | Implemented + designed | Implemented: versioned HTTP Cart API. Designed: Service Bus events, checkout saga, dead-letter handling and outbox |
| Main technology selection | Implemented + designed | .NET/C#/ASP.NET Core, PostgreSQL, React/TypeScript/Vite, Docker and CI are implemented; Azure platform choices are documented target-state decisions |
| Scaling strategy | Designed | `docs/architecture.md#scaling-and-resilience` |
| Security and authentication | Implemented + designed | Implemented anonymous-cart capability authorization and rate limits. Authenticated customer ownership through OIDC/OAuth 2.1 is a documented extension point, not a live identity-provider integration |
| Key components and responsibilities | Designed | Component responsibility/consistency table and two-team ownership model |
| External integration such as tax authority | Designed | Anti-corruption adapter pattern, synchronous/legal decision rule, queues, idempotency, replay and audit design. No live tax-authority credentials or fake adapter are committed |
| Monitoring, alerting and health checks | Implemented + designed | Implemented: structured logs, OpenTelemetry, Prometheus `/metrics`, `/health/live`, database-aware `/health/ready`. Designed: Azure Monitor/Application Insights dashboards, SLOs, alert policies and runbooks |
| Code-delivery plan | Implemented + designed | Implemented: GitHub Actions CI with locked restore, format, tests, browser smoke, audits, container build/scan, SBOMs and SHA-tagged image publishing after merge. Designed: environment promotion, migration job, canary/blue-green rollout and rollback |
| CI/CD | Implemented | `.github/workflows/ci.yml`, `scripts/ci/compose-smoke.sh`, `scripts/ci/nuget-audit.sh`, container builds/scans, SBOM artifacts and GHCR SHA image publication on `main` |
| Branching strategy | Implemented as exercise workflow + documented | Short-lived `codex/*` PR branches, protected `main`, conventional commits and reviewed pull requests |
| Minimal cart Web API | Implemented | Create/get cart, add/update/remove/clear-item endpoints, OpenAPI/Swagger, validation and RFC 9457 Problem Details |
| Database included | Implemented | EF Core PostgreSQL model, migrations, Docker Compose database, Testcontainers integration tests and Neon deployment |
| Selected production-minded requirements | Implemented | Optimistic concurrency, durable request-aware idempotency, rate limiting, token authorization, PostgreSQL-backed correctness, OpenTelemetry, health checks, CI scans and browser smoke tests |
| Online repository | Implemented | <https://github.com/KirilZafirov/store> |
| Incremental commits and PRs | Implemented | Separate PR slices for idempotency, validation, retryable frontend, contract/concurrency tests, cache removal, database/rate limits, frontend/e2e, CI and documentation |
| README with startup instructions | Implemented | Root `README.md`: Docker and local startup, migrations, verification, API examples, deployment and troubleshooting |

## Public demonstration evidence

- React/TypeScript storefront: <https://atlas-cart-store.vercel.app>
- Containerized .NET API and Swagger: <https://atlas-cart-api-kiril.onrender.com/swagger>
- PostgreSQL persistence: Neon PostgreSQL in Frankfurt
- Public deployment path: Neon for PostgreSQL, Render for the API container, Vercel for the storefront

## Deliberate limitations

- No live identity provider is wired. Anonymous capability tokens demonstrate authorization and an extension point for authenticated ownership/merge.
- No live payment, tax-authority, marketplace, inventory reservation or message broker integration is wired. These are target architecture with ports/adapters and failure policies.
- No multi-region deployment, autoscaling rules, production dashboards, alert rules, image signing or real Service Bus/outbox implementation is claimed.
- Redis was removed from the executable slice because it did not avoid the PostgreSQL source-of-truth read. It remains a target option only for justified projections, sessions, distributed rate limiting or hot data.
