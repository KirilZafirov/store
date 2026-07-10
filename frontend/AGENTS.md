# React Frontend Agent Guide

## Scope

These instructions apply to all work under `frontend/`. This application is a focused React and TypeScript demonstration client for the Cart API, not a full webshop.

## Stack and commands

- Use React, TypeScript, Vite, and the existing CSS approach.
- Install reproducibly with `npm ci`; do not replace the lockfile casually.
- Before completing a frontend change, run:

  ```bash
  npm run lint
  npm test
  npm run build
  ```

- Use `npm run dev` for local development and `npm run preview` to inspect a production build.

## Implementation conventions

- Keep TypeScript strict and avoid `any`. Model API payloads in `src/types.ts` or beside the feature when the type is feature-specific.
- Keep HTTP and storage details inside `src/api.ts`; components should consume typed operations rather than call `fetch` directly.
- Treat the server response as authoritative for cart contents, totals, version, currency, and timestamps.
- Include the opaque cart token on protected cart requests, the current version on mutations, and a fresh `Idempotency-Key` for each new user intent. A retry of the same request must reuse its key.
- On `409 Conflict`, retrieve the latest cart and preserve a clear retry path for the user. Do not silently overwrite concurrent changes.
- Never calculate authoritative prices in the UI. Seed prices are demonstration snapshots and checkout would revalidate them with the Pricing service.
- Keep components accessible: semantic controls, visible focus states, associated labels, keyboard operation, and status/error text announced appropriately.
- Preserve responsive behavior and explicit loading, empty, validation, conflict, retry, and unavailable-service states.
- Read the API base URL from `VITE_API_URL`; do not hard-code production or local origins.
- Do not place secrets in `VITE_*` variables or commit `.env` and `.vercel` files.

## Testing expectations

- Add or update Testing Library tests for user-visible behavior, not implementation details.
- Cover the success path and relevant loading, empty, validation, conflict, and service-error states.
- Mock at the API boundary. Avoid tests coupled to internal hook calls or CSS class names.
- For a meaningful integration change, smoke-test create, add, update, remove, and clear against the running API.

## Change boundaries

- Keep this UI intentionally small. Do not introduce global state libraries, component frameworks, or generated clients unless the added complexity is justified by a concrete requirement.
- When changing an API contract, update the .NET contract, frontend types/client, tests, OpenAPI expectations, and documentation in the same logical change.

