# RetailFlow — Comprehensive Engineering Audit Report

**Audit Date:** `2026-11-26`  
**Verified Commit SHA:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d` (`main`)

---

## 1. Executive Summary
RetailFlow has progressed from an experimental PoC + Product Host bootstrap to a working **Modular Monolith** implementing **Vertical Slice 01 (Public Store Catalog Read API)** on `.NET 10 LTS` (`10.0.15`) and SQL Server. All production code, EF Core migrations, and SQL Server integration tests are isolated from `experiments/`. Zero Critical or High code defects were found in the current production slice; 6 Low/Informational findings (including outdated bootstrap documentation, collation dependence, MARS savepoint warning, root patch file hygiene, partial ISO currency seed scope, and absence of a standalone unit-test project) are recorded in [Findings.csv](Findings.csv).

## 2. Repository Baseline
- **Repository:** `ayhm325/RetailFlow`
- **Branch / HEAD:** `main` at `04392c875c275e99f4b5ee378f63b3a8b8fc541d`
- **SDK:** `.NET 10.0.404` (`global.json`, `rollForward: latestPatch`)
- **Initial Working Tree:** Clean (`git status --short` empty at audit start)

## 3. Technologies and Versions
- **Runtime / Web:** `.NET 10.0` (`net10.0`), `Microsoft.AspNetCore.OpenApi` `10.0.15`, `Microsoft.AspNetCore.Mvc.Testing` `10.0.15`
- **Persistence:** `Microsoft.EntityFrameworkCore.SqlServer` `10.0.15`, `Microsoft.EntityFrameworkCore.Design` `10.0.15`, `dotnet-ef` `10.0.15`, SQL Server 2022 (CI container `mcr.microsoft.com/mssql/server:2022-latest`)
- **PoC Security:** `Microsoft.AspNetCore.Identity.EntityFrameworkCore` `10.0.15`, `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` `10.0.15`
- **Testing:** `Microsoft.NET.Test.Sdk` `17.14.1`, `xunit` `2.9.3`, `xunit.runner.visualstudio` `3.1.4`, Playwright Chromium

## 4. Repository Structure
- `src/`: `RetailFlow.Domain`, `RetailFlow.Application`, `RetailFlow.Infrastructure`, `RetailFlow.Api` (`RetailFlow.slnx`)
- `tests/`: `RetailFlow.Api.IntegrationTests`
- `experiments/`: `IdentitySpike/` (`RetailFlow.IdentitySpike.slnx`) and `SessionTransportSpike/` (`RetailFlow.SessionTransportSpike.slnx`, `RetailFlow.SessionTransportSpike.Playwright.slnx`)
- `.github/workflows/ci.yml`: Automated Release build, EF migrations, SQL integration tests, and Playwright E2E
- `docs/`: Reconstructed engineering documentation hierarchy

## 5. Architecture Assessment
- Clean Architecture / Modular Monolith layering (`Domain` <- `Application` <- `Infrastructure` <- `Api`) is strictly enforced with zero circular dependencies and zero references to `experiments/`.
- `RetailFlow.Api` acts purely as Composition Root and HTTP transport.
- `ConnectionStringResolver` separates runtime `IConfiguration`-driven resolution (with non-Development fail-fast via `ValidateOnStart()`) from design-time CLI fallback.

## 6. Business and Functional Assessment
- **Implemented:** Active store resolution by slug, active category listing, published product search/filter/sort/pagination, product detail lookup with ordered images and derived availability (`OnHand - Reserved`), `Currency` ISO 4217 validation, `MoneyPrecision` enforcement in `Product.Create`, and `ST-03` pickup restriction (`Physical` stores only).
- **Planned:** Merchant/Admin write operations, Cart, Checkout, Orders, Fulfillment zones, React SPA, and production Auth integration.

## 7. Database Assessment
- Dedicated `RetailFlowProduct` database with 2 migrations (`InitialCatalogSlice`, `HardenCurrencyConstraints`), 8 tables (including `__EFMigrationsHistory`), 7 `Restrict` foreign keys (including composite tenant FKs `(StoreId, CategoryId)` and `(StoreId, ProductId)`), 6 `UX_*` unique indexes, and 10 SQL `CHECK` constraints (including `Latin1_General_BIN2` uppercase check on `Currencies.Code`).
- `Products.Price` uses `decimal(19,4)` alongside `Currency.MinorUnit` domain validation.

## 8. Identity and Security Assessment
- Production `RetailFlow.Api` intentionally has no Authentication, Authorization, Identity, or CORS enabled; `AUTH-ADR-01` remains `Proposed`.
- Production tenant isolation is enforced server-side (`StoreResolutionEndpointFilter` -> `StoreDirectoryResolver` -> scoped `StoreRequestContextAccessor` -> `StoreId` query predicates -> SQL composite FKs).
- `AllowedHosts` startup validation rejects `*`, `0.0.0.0`, `[::]`, leading `.`, and empty values outside Development.
- `IdentitySpike` and `SessionTransportSpike` PoCs validate `ScopedUserStore`, `SqlServerTicketStore`, `BrowserCsrfProtectionMiddleware`, and Data Protection in `experiments/`.

## 9. API Assessment
- Endpoints follow REST conventions, bind `ValidationProblemDetails` (400) and `ProblemDetails` (404) cleanly, bound `pageSize` (1..50), use `long` pagination offset to avoid `int` overflow, and exclude EF entities and raw inventory counts (`OnHand`, `Reserved`).
- `/health/live` is process-only; `/health/ready` checks SQL Server connectivity via `DatabaseReadinessHealthCheck` and propagates `OperationCanceledException` on cancellation.

## 10. Testing Assessment
- All tests run against real SQL Server (no EF InMemory/SQLite relational substitutes) and Chromium Playwright.
- Latest verified Release suite: `53` (`RetailFlow.slnx`) + `49` (`IdentitySpike`) + `90` (`SessionTransportSpike`) + `4` (`Playwright`) = **196 / 196 Passed** (verified in current session).

## 11. Performance and Scalability Assessment
- Catalog queries use `AsNoTracking()`, SQL-side `Select` projections, deterministic `.ThenBy(p => p.Id)` ordering, and aligned composite indexes (`IX_Products_StoreId_Status_*`). No N+1 queries or premature materialization exist.

## 12. Official Documentation Verification
Verified against Microsoft Learn (.NET 10 / EF Core 10 / ASP.NET Core 10), OWASP Cheat Sheet Series, RFC 9457, and SIX ISO 4217 standards (see [Reference-Register.csv](Reference-Register.csv)).

## 13. Practitioner Reference Review
Clean Architecture dependency direction, Modular Monolith boundaries, and `decimal(19,4)` + `MinorUnit` money validation align with established .NET enterprise practices without over-engineering (no unnecessary generic repositories, MediatR, or distributed caches).

## 14. Documentation Accuracy Assessment
- `docs/product-host-bootstrap.md` contained 4 outdated bootstrap statements (now updated).
- `docs/public-store-catalog-slice.md`, `docs/AUTH-ADR-01-Session-Transport.md`, and `docs/phase-06-identity-spike-status.md` were accurate and preserved.
- Missing root `README.md`, `docs/README.md`, architecture diagrams, traceability CSVs, and ADRs were created.

## 15. Critical Findings
- **Critical / High:** None remaining in current production codebase.
- **Low / Informational (`AUD-01`..`AUD-06`):** Documented in [Findings.csv](Findings.csv).

## 16. Technical Debt
1. `RetailFlow-Catalog-Hardening.patch` tracked at repo root (`AUD-04`).
2. Default SQL Server `CI` collation dependency for search (`AUD-02`).
3. `MultipleActiveResultSets=True` disabling EF Core savepoints (`AUD-03`).
4. Partial 8-currency ISO 4217 seed list (`AUD-05`).
5. Absence of standalone `RetailFlow.UnitTests` project (`AUD-06`).

## 17. Production Readiness Assessment
- **Vertical Slice 01 (Read-Only Public Catalog):** Ready for controlled staging/preview once Azure App Service provisioning and live `ForwardedHeaders` verification are completed.
- **Full RetailFlow MVP:** Not yet production-ready (pending Auth integration, Merchant/Admin APIs, Cart/Checkout/Orders, React frontend, and Azure deployment).

## 18. Prioritized Recommendations
1. **P2:** Test removing `MultipleActiveResultSets=True` from Product Host connection strings before implementing write transactions.
2. **P2:** Complete `AUTH-ADR-01` production-readiness gates (load benchmark, Data Protection cert rotation runbook, Azure Staging `ForwardedHeaders` check).
3. **P3:** Remove `RetailFlow-Catalog-Hardening.patch` from repo root in a cleanup PR; expand ISO 4217 currency seed list before global onboarding; add `RetailFlow.UnitTests` for Domain/Application.

## 19. Known Limitations
- No live Azure App Service environment exists yet to verify `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` header sanitization.
- Production Auth/Identity, Cart, Checkout, and Orders are not yet implemented in `src/`.

## 20. Final Conclusion
RetailFlow's production foundation (`RetailFlow.slnx`) is clean, well-bounded, tenant-isolated, and backed by 196 passing Release tests. Documentation is now reconstructed, traced to code and authoritative references, and ready for ongoing engineering delivery.
