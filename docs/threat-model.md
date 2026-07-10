# Cart threat model

| Threat | Exposure | Mitigation and verification |
|---|---|---|
| Stolen anonymous token | Token grants cart access | 256-bit entropy, stored hash only, TLS, no token logs, rotation/merge after login; test invalid token isolation |
| Broken object authorization | Caller changes cart ID | Verify the token against that cart on every operation; integration test cross-cart access |
| Replay / duplicate write | Networks and clients retry | Required idempotency key stored atomically with mutation; test same key twice |
| Lost update | Two tabs change one cart | Expected cart version and database concurrency token; return 409 and refresh |
| Injection / malformed input | Public JSON and route values | Typed binding, allow-list validation, EF parameterization, limits and Problem Details |
| Resource exhaustion | Cart/item spam or expensive requests | Edge and service rate limits, quantity/size caps, timeouts, quotas and autoscaling |
| Secret or personal-data leakage | Logs, traces, events and CI | Structured allow-listed fields, redaction, Key Vault, managed identities, scanning and retention policy |
| Cache poisoning / stale reads | Shared Redis | Per-cart keys, authoritative version check, short TTL, cache invalidation, database fallback |
| Dependency compromise | NuGet/npm/container supply chain | Locks, audit gates, Dependabot, SBOM/signing in release pipeline and minimal runtime images |
| Privileged misuse | Support and operator access | Least privilege, MFA, just-in-time access, immutable audit events and separation of duties |

The anonymous capability model is suitable for a demonstration cart, not a replacement for authenticated ownership. The React demo keeps it in browser storage to make the exercise self-contained; a production web BFF should prefer a `Secure`, `HttpOnly`, `SameSite` cookie and a strict CSP to reduce token theft through XSS. After login, the service must authorize the subject, merge through a versioned command, rotate the capability, and retain an auditable ownership transition.
