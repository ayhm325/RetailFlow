# RetailFlow — Current State Snapshot

> Snapshot date: 2026-11-26  
> Observed repository: `ayhm325/RetailFlow`  
> Observed branch: `main`  
> Observed HEAD: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`  
> Confidence: repository metadata/tree and selected source documents inspected through GitHub; no local build/test was run during package preparation.

## 1. Verified repository shape at snapshot
- Root contains `RetailFlow.slnx`, `global.json`, `.github/workflows/ci.yml`, `src/`, `tests/`, `experiments/`, and `docs/`.
- No root `README.md` or `AGENTS.md` was present in the inspected root tree at this snapshot.
- `RetailFlow.slnx` includes:
  - `src/RetailFlow.Api`
  - `src/RetailFlow.Application`
  - `src/RetailFlow.Domain`
  - `src/RetailFlow.Infrastructure`
  - `tests/RetailFlow.Api.IntegrationTests`
- `global.json` pins .NET SDK `10.0.404` with `latestPatch` roll-forward.
- The current product solution and the experimental Identity/SessionTransport solutions are separate.

## 2. Product Host implementation facts
The repository's `docs/public-store-catalog-slice.md` describes the first implemented business slice:
- Public Store Catalog Read API.
- Domain model includes Currency, Store, StoreSettings, Category, Product, ProductImage, InventoryItem and related value/status types.
- Application contains StoreRequestContext, store resolution abstractions, catalog query and read DTO/service contracts.
- Infrastructure contains `RetailFlowDbContext`, SQL Server persistence, EF Core mappings, migration, and StoreDirectoryResolver.
- API exposes three public catalog GET endpoints:
  - `GET /api/stores/{slug}/catalog/categories`
  - `GET /api/stores/{slug}/catalog/products`
  - `GET /api/stores/{slug}/catalog/products/{productId:guid}`
- The catalog documentation describes database readiness health checks and tenant-scoped query behavior.
- Treat the documentation as a claim to re-verify against source before changing or declaring current behavior; this package preparation did not execute the tests.

## 3. Identity and session work — do not conflate with Product Host
- `experiments/IdentitySpike/` and `experiments/SessionTransportSpike/` are isolated experiments.
- The inspected `public-store-catalog-slice.md` explicitly says Product Host authentication/authorization is not configured and the PoCs are not referenced/shared with Product Host.
- `docs/AUTH-ADR-01-Session-Transport.md` header says the overall ADR is `Proposed`, while several scoped MVP decisions are marked adopted (including Same-Origin and hosting choices). Preserve this distinction; do not mark the whole ADR Accepted.
- The Phase 06 status document reports Identity and SessionTransport test counts and completed spike work. These are experiment evidence, not Product Host integration evidence.

## 4. Explicitly not established by this snapshot
The inspected baseline docs say the following are deferred/not implemented in Product Host at the snapshot:
- ASP.NET Core Identity integration, Authentication, Authorization.
- Merchant/Admin catalog mutations.
- Shopping Cart, Checkout, Orders, Payments.
- React frontend/ClientApp.
- Production Azure deployment workflow.

Do not treat a target chapter, roadmap row, diagram, or spike as proof these exist. Re-check the actual repository before each task.

## 5. CI and security observation
- `.github/workflows/ci.yml` builds Product Host and both PoC solutions, applies EF migrations to SQL Server service databases, runs integration suites and Playwright tests, and uploads test diagnostics.
- The inspected workflow contains a literal SQL Server SA test password in workflow configuration/environment. Do not reproduce it in docs or logs. Treat it as a hard-coded CI credential hygiene issue: replace with a per-run generated value or appropriately scoped secret; rotate it if it has ever been reused outside disposable CI.
- The existence of a CI workflow does not prove that the latest run passed. Query the actual run and commit status before claiming success.

## 6. Source files inspected for this snapshot
- `RetailFlow.slnx`
- `global.json`
- `.github/workflows/ci.yml`
- `docs/AUTH-ADR-01-Session-Transport.md`
- `docs/phase-06-identity-spike-status.md`
- `docs/product-host-bootstrap.md`
- `docs/public-store-catalog-slice.md`
- GitHub recursive tree for `main`

## 7. Required refresh
At the beginning of every substantial session, compare this snapshot SHA to actual HEAD. If it differs, inspect the intervening changes and refresh this file only from evidence.
