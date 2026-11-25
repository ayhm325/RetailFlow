# RetailFlow — Vertical Slice 01: Public Store Catalog Read API

## Scope and Status

This document records the first production business vertical slice in `RetailFlow.slnx`: **Public Store Catalog Read API**.

### Implemented

- **Domain layer (`src/RetailFlow.Domain`):**
  - `Currency` (global ISO 4217 reference data: `Code`, `NumericCode`, `MinorUnit`, `IsActive`; not tenant-owned)
  - `MoneyPrecision` (validates non-negative monetary amounts against `Currency.MinorUnit` without implicit rounding)
  - `Store`, `StoreType`, `StoreStatus`, `StoreSettings` (1:1 per store: `Id`, `StoreId`, `CurrencyCode`, `TimeZoneId`, `DeliveryEnabled`, `PickupEnabled`)
  - `Category`, `Product` (requires `Currency` and enforces `MoneyPrecision.EnsureValidAmount` in `Product.Create`), `ProductStatus`, `ProductImage`
  - `InventoryItem` (`Available => OnHand - Reserved`, `IsAvailable => Available > 0`, `RowVersion`)
- **Application layer (`src/RetailFlow.Application`):**
  - `StoreRequestContext`, `IStoreRequestContextAccessor`, `IStoreDirectoryResolver`
  - `CatalogProductsQuery` (defaults: `Page = 1`, `PageSize = 20`, `MaxPageSize = 50`, `MaxSearchLength = 100`, sort options: `newest`, `price_asc`, `price_desc`, `name_asc`, `name_desc`)
  - `IPublicCatalogQueryService` and read DTOs (`CatalogCategoriesResponse`, `CatalogProductsResponse`, `CatalogProductDetailsDto`)
- **Infrastructure layer (`src/RetailFlow.Infrastructure`):**
  - `RetailFlowDbContext` and `RetailFlowDbContextFactory` targeting the dedicated `RetailFlowProduct` SQL Server database (`RETAILFLOW_CONNECTION` / `ConnectionStrings:RetailFlow`)
  - `ConnectionStringResolver`: runtime resolution checks non-whitespace `RETAILFLOW_CONNECTION`, then `ConnectionStrings:RetailFlow`, allowing the local SQL Server fallback in `Development` only and failing fast on startup in non-Development environments without logging secrets; design-time resolution handles empty/whitespace environment variables cleanly
  - EF Core entity configurations with `Currencies` (PK `Code`, `CK_Currencies_*`), `StoreSettings` (`UX_StoreSettings_StoreId`, `FK_StoreSettings_Stores_StoreId`, `FK_StoreSettings_Currencies_CurrencyCode`), composite tenant-isolation foreign keys `(StoreId, CategoryId)` and `(StoreId, ProductId)`, filtered unique index `UX_Products_StoreId_Sku WHERE [Sku] IS NOT NULL`, `decimal(19,4)` price precision, `datetimeoffset(3)` timestamps, `smallint` enum storage, and SQL `CHECK` constraints
  - **Currency Seed Data:** Seeded in `CurrencyConfiguration` via EF Core `HasData` using SIX Financial Information ISO 4217 List One (reviewed 2026-11-25): `JOD` (400, 3), `KWD` (414, 3), `SAR` (682, 2), `AED` (784, 2), `USD` (840, 2), `EUR` (978, 2), `GBP` (826, 2), `JPY` (392, 0). Deactivating a currency (`IsActive = false`) blocks new `StoreSettings` assignments while existing stores referencing that currency remain readable.
  - Initial EF Core migration `InitialCatalogSlice`
  - `StoreDirectoryResolver` resolves `StoreStatus.Active` stores with `StoreSettings` and `Currency`
  - `PublicCatalogQueryService` uses `AsNoTracking()` SQL projections, enforces `Category.IsActive` on both `GetProductsAsync` and `GetProductDetailsAsync`, and uses `long` pagination offset calculation with deterministic `.ThenBy(p => p.Id)` ordering
- **API layer (`src/RetailFlow.Api`):**
  - `DatabaseReadinessHealthCheck` registered under tag `"ready"` (`RetailFlowDbContext.Database.CanConnectAsync`), returning `200 Healthy` when reachable and `503 Unhealthy` without leaking connection strings when unreachable; `/health/live` remains process-liveness-only (`Predicate = _ => false`)
  - `StoreResolutionEndpointFilter` resolving `{slug}` for `StoreStatus.Active` stores and returning `404` RFC 9457 `ProblemDetails` otherwise
  - `PublicCatalogEndpoints` mapped before `MapFallback`:
    - `GET /api/stores/{slug}/catalog/categories`
    - `GET /api/stores/{slug}/catalog/products`
    - `GET /api/stores/{slug}/catalog/products/{productId:guid}`
- **Integration tests (`tests/RetailFlow.Api.IntegrationTests`):**
  - Real SQL Server integration tests in `PublicStoreCatalogIntegrationTests.cs` covering health checks (`/health/live`, `/health/ready` reachable & unreachable), `Currency` & `StoreSettings` SQL constraints and lifecycle, `MoneyPrecision` validation, store resolution, inactive category hiding & reactivation, search/filter/sort/pagination (including `int.MaxValue` page), concurrent multi-store isolation, `ValidationProblemDetails`, connection string priority/fail-fast, and production error-detail hiding
- **CI (`/.github/workflows/ci.yml`):**
  - Triggers on `push` to `main` and `feature/**`, `pull_request` targeting `main`, and `workflow_dispatch`
  - Applies `RetailFlowDbContext` migrations to `RetailFlowProduct` on the CI SQL Server 2022 service and executes `RetailFlow.slnx` integration tests alongside all PoC and Playwright suites

### Proposed (Reference Only, Not Activated)
- `AUTH-ADR-01` session transport architecture remains `Proposed` and unchanged.
- Phase 05 / `MNY-01` remain `Proposed` in the architecture documents and were not altered.

### Not Implemented / Deferred
- ASP.NET Core Identity, Authentication, Authorization, `UseAuthentication`, and `UseAuthorization` remain unconfigured in `RetailFlow.Api`.
- No references or code shared with `experiments/IdentitySpike/` or `experiments/SessionTransportSpike/`.
- Merchant/Admin catalog mutations, Shopping Cart, Checkout, Orders, Payments, and Azure deployment workflows are deferred to later slices.
