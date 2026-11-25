# RetailFlow — Current State Snapshot

> Snapshot date: 2026-11-26  
> Observed repository: `ayhm325/RetailFlow`  
> Observed `main` HEAD: `e204c9e6f0d570be24fbdb2750889dd464d1e077` (PR #3 merged)  
> Active working branch: `feature/public-store-catalog-read` (`d867bc2` + local Gate 1 corrective pass)  
> Verified Product Host code/test snapshot: `04392c875c275e99f4b5ee378f63b3a8b8fc541d` (PR #2, PR #3, and subsequent commits modified documentation/governance/scripts only)

## 1. Verified repository shape at current HEAD
- Root contains `RetailFlow.slnx`, `global.json`, `.github/workflows/ci.yml`, `README.md`, `AGENTS.md`, `CLAUDE.md`, `GEMINI.md`, `src/`, `tests/`, `experiments/`, `docs/`, `scripts/`, and `tools/`.
- `RetailFlow.slnx` includes:
  - `src/RetailFlow.Api`
  - `src/RetailFlow.Application`
  - `src/RetailFlow.Domain`
  - `src/RetailFlow.Infrastructure`
  - `tests/RetailFlow.Api.IntegrationTests`
  - `tests/RetailFlow.Domain.UnitTests`
- `global.json` pins .NET SDK `10.0.404` with `latestPatch` roll-forward.
- The current product solution (`RetailFlow.slnx`) and the experimental Identity/SessionTransport solutions (`experiments/IdentitySpike/`, `experiments/SessionTransportSpike/`) remain strictly separate.

## 2. Product Host implementation facts (verified on `feature/public-store-catalog-read`)
- **Vertical Slice 01 — Public Store Catalog & Identity Read API** is implemented and verified (`6/6` Domain unit tests + `57/57` SQL integration tests):
  - Domain model includes `Currency`, `Store`, `StoreSettings`, `Category`, `Product`, `ProductImage`, `InventoryItem`, `DeliveryZone`, `MoneyPrecision`, and related value/status types.
  - Application contains `StoreRequestContext`, store resolution abstractions, catalog query and read DTO/service contracts (`PublicStoreIdentityDto`, `PublicDeliveryZonesResponse`).
  - Infrastructure contains `RetailFlowDbContext`, SQL Server persistence, EF Core mappings, 3 migrations (including `20261125232250_AddStoreBrandingAddressesAndDeliveryZones`), `ConnectionStringResolver`, and `StoreDirectoryResolver`.
  - API exposes `/health/live`, `/health/ready`, and five public read GET endpoints:
    - `GET /api/stores/{slug}`
    - `GET /api/stores/{slug}/delivery-zones`
    - `GET /api/stores/{slug}/catalog/categories`
    - `GET /api/stores/{slug}/catalog/products`
    - `GET /api/stores/{slug}/catalog/products/{productId:guid}`

## 3. Identity and session work — do not conflate with Product Host
- `experiments/IdentitySpike/` and `experiments/SessionTransportSpike/` are isolated experiments.
- Product Host authentication/authorization is not configured, and the PoCs are not referenced by `RetailFlow.slnx`.
- `docs/AUTH-ADR-01-Session-Transport.md` overall status remains `Proposed` (`v0.14`), while specific MVP decisions are marked adopted inside the draft.
- PoC test suites are experiment evidence only, not Product Host integration evidence.

## 4. Explicitly not established in Product Host
- ASP.NET Core Identity integration, Authentication, Authorization.
- Merchant/Admin catalog & store write APIs.
- Shopping Cart, Checkout, Orders, Payments, and DeliveryZone/Merchant write APIs.
- React frontend/ClientApp (`wwwroot` SPA assets).
- Production Azure deployment workflow.

## 5. Documentation & Governance Baseline (updated at PR #3 / `e204c9e`)
- Comprehensive audit & documentation reconstruction (`docs/00-Project-Overview/` through `docs/09-Audit-and-Traceability/`) merged via PR #2 (`b73d30c`).
- Gate 1 Requirements & Business Baseline (`AUD-001`, `AUD-002`, `AUD-003`, `AUD-007`) plus AI Operating Memory (`AGENTS.md`, `.agents/`, `docs/engineering/`) merged via PR #3 (`e204c9e`) after `"Build, SQL Integration & Playwright E2E"` passed on `2c020de`.
- Automated validators: `scripts/validate-gate1-baseline.ps1` (`773/773` checks pass) and `tools/validate_operating_memory.py` (pass).

## 6. CI and security observation
- `.github/workflows/ci.yml` builds Product Host and both PoC solutions, applies EF migrations to SQL Server service containers, runs integration suites and Playwright tests, and uploads test diagnostics.
- **CI Hygiene (`AUD-015` — Remediated on branch):** `.github/workflows/ci.yml` now generates a masked per-run ephemeral SQL Server SA password in a `prepare` job instead of a literal password.

## 7. Required refresh
At the beginning of every substantial session, compare `e204c9e6f0d570be24fbdb2750889dd464d1e077` / `2c020de` to actual `HEAD`. If it differs, inspect the intervening changes and refresh this file only from evidence.
