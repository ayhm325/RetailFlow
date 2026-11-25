# Testing & Verification Strategy

**Last Verified:** `2026-11-26` | **Branch:** `feature/public-store-catalog-read`

## 1. Test Solutions & Verified Counts (Release)

All four solutions target `.NET 10` (`10.0.15`), `xUnit 2.9.3`, and execute against real **SQL Server** (no EF InMemory or SQLite substitutes for relational constraints):

| Solution | Test Assemblies | Latest Verified Release Result |
|---|---|---|
| [RetailFlow.slnx](../../RetailFlow.slnx) | `RetailFlow.Domain.UnitTests` (6) + `RetailFlow.Api.IntegrationTests` (57) | **63 Passed, 0 Failed, 0 Skipped** |
| [RetailFlow.IdentitySpike.slnx](../../experiments/IdentitySpike/RetailFlow.IdentitySpike.slnx) | `IdentityScopeUnitTests` (14) + `IdentitySqlServerIntegrationTests` (35) | **49 Passed, 0 Failed, 0 Skipped** |
| [RetailFlow.SessionTransportSpike.slnx](../../experiments/SessionTransportSpike/RetailFlow.SessionTransportSpike.slnx) | `UnitTests` (63) + `IntegrationTests` (27) | **90 Passed, 0 Failed, 0 Skipped** |
| [RetailFlow.SessionTransportSpike.Playwright.slnx](../../experiments/SessionTransportSpike/RetailFlow.SessionTransportSpike.Playwright.slnx) | `PlaywrightTests` (Chromium E2E) | **4 Passed, 0 Failed, 0 Skipped** |
| **Total** | | **206 / 206 Passed** |

## 2. Production Test Coverage (`RetailFlow.Api.IntegrationTests`)
- **Host & Security ([ProductHostIntegrationTests.cs](../../tests/RetailFlow.Api.IntegrationTests/ProductHostIntegrationTests.cs)):** `AllowedHosts` startup rejection (`*`, `0.0.0.0`, `[::]`, leading `.`, empty/whitespace), `ForwardedHeaders`, SPA `Accept` header content negotiation (`Vary: Accept`), and `ProblemDetails` exception hiding.
- **Catalog, Currency, Health & SQL Constraints ([PublicStoreCatalogIntegrationTests.cs](../../tests/RetailFlow.Api.IntegrationTests/PublicStoreCatalogIntegrationTests.cs)):**
  - `/health/live` DB-independence and `/health/ready` SQL connectivity + CI `RETAILFLOW_CONNECTION` override + cancellation propagation.
  - `ConnectionStringResolver` priority and non-Development fail-fast.
  - `MoneyPrecision` (`JOD`, `KWD`, `USD`, `EUR`, `JPY`), `StoreSettings` currency binding, and `ST-03` pickup rule (`Physical` vs `OnlineOnly`).
  - SQL constraints: `UX_Currencies_NumericCode`, `CK_Currencies_Code_Format` (`Latin1_General_BIN2` rejecting lowercase), `FK_StoreSettings_Currencies_CurrencyCode`, `UX_StoreSettings_StoreId`, `UX_Stores_Slug`, `UX_Products_StoreId_Sku`, composite FKs, and inventory `CHECK` constraints.
  - Inactive store/category exclusion, category reactivation, pagination overflow (`int.MaxValue`), concurrent multi-store isolation, and `ValidationProblemDetails`.

## 3. Gaps
- Dedicated unit-test assembly `RetailFlow.Domain.UnitTests` (`DomainInvariantsTests`) is now added to `RetailFlow.slnx` (resolving `AUD-06`), while `RetailFlow.Application` unit tests will be added alongside authenticated Application handlers.
- No load/performance benchmark suite exists yet.
