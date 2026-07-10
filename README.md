# Atlas Retail Cart

An interview-sized vertical slice for a global retail platform: a production-minded ASP.NET Core cart API, PostgreSQL persistence, optional Redis cache, OpenTelemetry instrumentation, and a focused React client.

The repository deliberately demonstrates **depth over pretend breadth**. The wider event-driven Azure architecture is specified in [docs/architecture.md](docs/architecture.md); the executable code proves the cart boundary end to end.

## Quick start with Docker

Prerequisites: Docker Desktop with Compose v2. No host .NET or Node installation is required.

```bash
docker compose up --build
```

Then open:

- Storefront: <http://localhost:5173>
- Swagger UI: <http://localhost:8080/swagger>
- OpenAPI JSON: <http://localhost:8080/openapi/v1.json>
- Liveness: <http://localhost:8080/health/live>
- Readiness: <http://localhost:8080/health/ready>
- Prometheus metrics: <http://localhost:8080/metrics>

PostgreSQL data persists in the `cart-data` volume. Stop services with `docker compose down`; include `--volumes` only when intentionally deleting local cart data.

## Local development

Pinned toolchains are .NET SDK 10 (see `global.json`) and Node.js 24.

Start dependencies:

```bash
docker compose up -d postgres redis
```

Run the API:

```bash
export ConnectionStrings__CartDatabase='Host=localhost;Port=55432;Database=cart;Username=cart;Password=cart'
export ConnectionStrings__Redis='localhost:56379'
export ApplyMigrations=true
dotnet run --project src/Cart.Api
```

Run the UI in a second terminal:

```bash
cd frontend
npm ci
npm run dev
```

The included migration is applied automatically only when `ApplyMigrations=true`. Production runs migrations as a controlled deployment job, not from every API replica. To create a future migration:

```bash
dotnet ef migrations add ChangeName --project src/Cart.Infrastructure --startup-project src/Cart.Api
```

## API example

Create an anonymous cart:

```bash
curl -i -X POST http://localhost:8080/api/v1/carts
```

The response contains `cart.id` and a one-time-visible `accessToken`. Supply them on every later request. Mutations also require the current cart `version` and a unique retry key:

```bash
curl -X POST http://localhost:8080/api/v1/carts/CART_ID/items \
  -H 'Content-Type: application/json' \
  -H 'X-Cart-Token: ACCESS_TOKEN' \
  -H 'Idempotency-Key: add-keyboard-001' \
  -d '{"productId":"10000000-0000-0000-0000-000000000002","name":"Contour keyboard","unitPrice":89.00,"currency":"EUR","quantity":1,"version":0}'
```

Prices in this demo are cart snapshots supplied by the client. A production Cart service accepts a product/offer reference and obtains authoritative price data from Pricing; Checkout revalidates it before committing an order.

## Verification

Backend build and domain tests:

```bash
dotnet build RetailPlatform.slnx
dotnet test tests/Cart.Domain.Tests
```

API integration tests start a disposable PostgreSQL container, so Docker must be available:

```bash
dotnet test tests/Cart.Api.Tests
```

Frontend checks:

```bash
cd frontend
npm ci
npm run lint
npm test
npm run build
```

Full-stack smoke test after `docker compose up --build`: create a cart in the UI, add the same product twice, change quantity, remove it, add another product, clear the cart, then confirm `/health/ready` and `/metrics` respond.

## Design highlights

- Domain aggregate enforces quantities, one currency per cart, decimal money, line totals and subtotal.
- PostgreSQL is authoritative; Redis is cache-aside and failures degrade performance rather than correctness.
- A 256-bit opaque cart token is stored only as a SHA-256 hash and compared in constant time.
- Expected versions prevent lost updates; conflicts are `409` RFC 9457 Problem Details responses.
- Idempotency keys are committed in the same database transaction as mutations, making network retries safe.
- JSON logs, distributed traces, runtime/request metrics, rate limiting, readiness and liveness are wired at the API boundary.

The capability token models anonymous carts. The standalone demo persists it in browser storage; a production web BFF should issue a `Secure`, `HttpOnly`, `SameSite` cookie under a strict CSP. Authenticated ownership uses OIDC/OAuth 2.1, subject-based authorization, a versioned merge command, token rotation, and an ownership audit record.

## Repository map

```text
src/Cart.Domain          Aggregate and business invariants
src/Cart.Application     Use cases, ports and transport-neutral DTOs
src/Cart.Infrastructure  EF Core/PostgreSQL, Redis and security services
src/Cart.Api             HTTP contract, telemetry, health and error mapping
tests/                   Domain and container-backed integration tests
frontend/                React + TypeScript demonstration client
docs/                    Architecture, threat model and ADRs
.github/                 CI and dependency updates
```

## Architecture package

- [Architecture vision](docs/architecture.md)
- [Requirements traceability](docs/requirements-traceability.md)
- [Threat model](docs/threat-model.md)
- [ADR 0001 — modular first](docs/adr/0001-modular-first.md)
- [ADR 0002 — PostgreSQL cart](docs/adr/0002-postgresql-cart.md)
- [ADR 0003 — events and outbox](docs/adr/0003-events-and-outbox.md)
- [ADR 0004 — cache aside](docs/adr/0004-cache-aside.md)

## Delivery strategy

Use trunk-based development with protected `main`, short-lived `feature/*` branches, conventional commits, mandatory CI and review. Images are tagged with the immutable commit SHA. Promote the same artifact through environments, run compatible migrations separately, use canary traffic plus smoke/SLO gates, and roll back routing to the preceding image when gates fail.

Suggested reviewable commit sequence for this exercise is: scaffold and domain; persistence/API; tests; React client; observability/infrastructure; architecture and CI. The current workspace may be committed in those logical groups before submission.

## Public deployment

The public demonstration is deployed end to end:

- **Storefront:** <https://atlas-cart-store.vercel.app>
- **API / Swagger:** <https://atlas-cart-api-kiril.onrender.com/swagger>
- **API readiness:** <https://atlas-cart-api-kiril.onrender.com/health/ready>
- **API metrics:** <https://atlas-cart-api-kiril.onrender.com/metrics>
- **Database:** Neon PostgreSQL in `aws-eu-central-1` (Frankfurt)

The deployed topology is Neon PostgreSQL, Render for the containerized API, and Vercel for the Vite client:

1. Neon provides the pooled PostgreSQL connection string to Render as a protected environment variable.
2. Render builds the root `Dockerfile`, runs the API, applies the demonstration migration, and checks `/health/ready`.
3. Vercel builds `frontend` with `VITE_API_URL` set to the public Render service.
4. Render allows the exact Vercel production origin through CORS.
5. The production smoke test creates a cart, adds an item, changes quantity, and verifies the updated subtotal without browser errors.

The API normalizes Neon PostgreSQL URIs for Npgsql, binds to Render's injected `PORT`, and intentionally runs without Redis when `ConnectionStrings__Redis` is empty. For this single-instance demonstration `ApplyMigrations=true` is acceptable; a scaled production deployment must run migrations as a separate release job.

## Troubleshooting

- **Readiness is unhealthy:** wait for PostgreSQL, inspect `docker compose logs postgres api`, and verify the connection string.
- **UI reports unavailable:** confirm the API is listening on port 8080 and `AllowedOrigin` matches the browser origin.
- **A mutation returns 409:** fetch the latest cart and retry the user action with the returned version and a new idempotency key.
- **Redis is down:** cart requests remain correct and use PostgreSQL; cache-related latency may increase.
- **Ports are occupied:** override the host side of the port mappings in a Compose override file.

## Scope boundaries

No real identity provider, payment processor, tax authority, inventory reservation, or message broker is needed to run this slice. Their contracts, failure handling, security boundaries and delivery sequence are addressed in the architecture document without embedding fake production credentials or unreliable placeholder integrations.
