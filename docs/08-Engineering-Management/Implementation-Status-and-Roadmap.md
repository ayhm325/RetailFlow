# Implementation Status, Technical Debt & Roadmap

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Implementation Status Summary

| Area | Status | Notes |
|---|---|---|
| Product Host Foundation (`RetailFlow.Api`) | **Implemented & verified** | `ForwardedHeaders`, `AllowedHosts` validator, `ProblemDetails`, health probes, SPA fallback |
| Vertical Slice 01 — Public Store Catalog & Store Identity | **Implemented & verified** | `Domain`, `Application`, `Infrastructure`, `Api`, 3 EF migrations, 56 SQL integration tests + 6 domain unit tests (`GET /api/stores/{slug}` + catalog + `DeliveryZones` schema) |
| Identity PoC (`experiments/IdentitySpike/`) | **PoC verified (isolated)** | `ScopedUserStore`, 49 tests; not integrated into production |
| Session Transport PoC (`experiments/SessionTransportSpike/`) | **PoC verified (isolated)** | Cookie + `ITicketStore` + CSRF + Data Protection + Playwright, 94 tests |
| Merchant/Admin Catalog & Store Write APIs | **Planned** | Next vertical slice candidates |
| Cart, Checkout, Orders & Inventory Reservation | **Planned** | Requires `Orders` / `Cart` / `InventoryReservations` tables & concurrency tests |
| React + TypeScript Frontend | **Planned** | Host SPA fallback ready; `wwwroot` not yet populated |
| Azure Deployment Workflow | **Planned** | Hosting stack approved (`App Service Linux` + `.NET 10` + `OIDC`); workflow not created |

## 2. Known Technical Debt & Operational Issues
1. **Tracked Patch Artifact in Root (`AUD-04` — Resolved):** `RetailFlow-Catalog-Hardening.patch` was removed from the repository root during the repository structure cleanup pass.
2. **Search Collation Dependence:** `PublicCatalogQueryService` relies on SQL Server default `CI` collation for case-insensitive `.Contains(search)`; not explicitly enforced in schema/query.
3. **MARS Enabled in Connection Strings:** `MultipleActiveResultSets=True` disables EF Core savepoints (no current Product Host defect since catalog is read-only).
4. **Partial Currency Seed List:** Only 8 ISO 4217 currencies are seeded (`JOD`, `KWD`, `SAR`, `AED`, `USD`, `EUR`, `GBP`, `JPY`); full ISO 4217 list import needed before global store onboarding.
5. **Dedicated Domain Unit Test Project (`AUD-06` — Resolved):** `tests/RetailFlow.Domain.UnitTests` added to `RetailFlow.slnx` and passing 6/6 tests.
