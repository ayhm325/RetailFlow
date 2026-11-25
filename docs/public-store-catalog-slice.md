# RetailFlow — Vertical Slice 01: Public Store Catalog Read API

## Scope and Status

This document records the first production business vertical slice in `RetailFlow.slnx`: **Public Store Catalog Read API**.

### Implemented
- **Domain layer (`src/RetailFlow.Domain`):**
  - `Store`, `StoreType`, `StoreStatus`
  - `Category`, `Product`, `ProductStatus`, `ProductImage`
  - `InventoryItem` (with `Available => OnHand - Reserved` and `IsAvailable => Available > 0`)
- **Application layer (`src/RetailFlow.Application`):**
  - `StoreRequestContext`, `IStoreRequestContextAccessor`, `IStoreDirectoryResolver`
  - `CatalogProductsQuery` (defaults: `Page = 1`, `PageSize = 20`, `MaxPageSize = 50`, `MaxSearchLength = 100`, sort options: `newest`, `price_asc`, `price_desc`, `name_asc`, `name_desc`)
  - `IPublicCatalogQueryService` and read DTOs (`CatalogCategoriesResponse`, `CatalogProductsResponse`, `CatalogProductDetailsDto`)
- **Infrastructure layer (`src/RetailFlow.Infrastructure`):**
  - `RetailFlowDbContext` and `RetailFlowDbContextFactory` targeting the dedicated `RetailFlowProduct` SQL Server database (`RETAILFLOW_CONNECTION` / `ConnectionStrings:RetailFlow`)
  - EF Core entity configurations with composite tenant-isolation foreign keys `(StoreId, CategoryId)` and `(StoreId, ProductId)`, filtered unique index `UX_Products_StoreId_Sku WHERE [Sku] IS NOT NULL`, `decimal(19,4)` price precision, `datetimeoffset(3)` timestamps, `smallint` enum storage, and SQL `CHECK` constraints
  - Initial EF Core migration `20261125120204_InitialCatalogSlice`
  - `StoreDirectoryResolver`, `StoreRequestContextAccessor`, and `PublicCatalogQueryService` using `AsNoTracking()` SQL projections and deterministic `.ThenBy(p => p.Id)` ordering
- **API layer (`src/RetailFlow.Api`):**
  - `StoreResolutionEndpointFilter` resolving `{slug}` for `StoreStatus.Active` stores and returning `404` RFC 9457 `ProblemDetails` otherwise
  - `PublicCatalogEndpoints` mapped before `MapFallback`:
    - `GET /api/stores/{slug}/catalog/categories`
    - `GET /api/stores/{slug}/catalog/products`
    - `GET /api/stores/{slug}/catalog/products/{productId:guid}`
- **Integration tests (`tests/RetailFlow.Api.IntegrationTests`):**
  - Real SQL Server integration tests in `PublicStoreCatalogIntegrationTests.cs` covering store resolution, category/product tenant isolation, search/filter/sort/pagination, inventory availability projection, SQL constraints, `ValidationProblemDetails`, and production error-detail hiding
- **CI (`/.github/workflows/ci.yml`):**
  - Applies `RetailFlowDbContext` migrations to `RetailFlowProduct` on the CI SQL Server 2022 service and executes `RetailFlow.slnx` integration tests

### Proposed (Reference Only, Not Activated)
- `AUTH-ADR-01` session transport architecture remains `Proposed` and unchanged.

### Not Implemented / Deferred
- ASP.NET Core Identity, Authentication, Authorization, `UseAuthentication`, and `UseAuthorization` remain unconfigured in `RetailFlow.Api`.
- No references or code shared with `experiments/IdentitySpike/` or `experiments/SessionTransportSpike/`.
- Merchant/Admin catalog mutations, Shopping Cart, Checkout, Orders, Payments, and Azure deployment workflows are deferred to later slices.
