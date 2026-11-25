# Public Catalog API Reference

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

All endpoints are unauthenticated, read-only, mapped before SPA fallback in [PublicCatalogEndpoints.cs](../../src/RetailFlow.Api/Catalog/PublicCatalogEndpoints.cs), and guarded by [StoreResolutionEndpointFilter.cs](../../src/RetailFlow.Api/Catalog/StoreResolutionEndpointFilter.cs).

## 1. Store Resolution Rule
- `{slug}` is normalized (`Trim().ToLowerInvariant()`) and matched against `Stores` where `Status == StoreStatus.Active` and `StoreSettings` + `Currency` exist.
- Missing or non-active stores (`PendingApproval`, `Suspended`, `Rejected`) return `404 Not Found` (`application/problem+json`).

## 2. Catalog Endpoints

### `GET /api/stores/{slug}/catalog/categories`
- **Response (`200 OK`):** `CatalogCategoriesResponse` (`StoreName`, `StoreSlug`, `Categories[]` ordered by `SortOrder`, `Name`, `Id`).
- Excludes inactive categories (`IsActive == false`) and other stores.

### `GET /api/stores/{slug}/catalog/products`
- **Query Parameters:**
  - `page` (optional, default `1`, must be `>= 1`)
  - `pageSize` (optional, default `20`, must be `1..50`)
  - `search` (optional, max length `100`, parameterized `Contains` against `Name`, `Description`, `Sku`)
  - `categoryId` (optional `Guid`)
  - `categorySlug` (optional `string`, composed with `AND` if `categoryId` is also supplied)
  - `sort` (optional: `newest` | `price_asc` | `price_desc` | `name_asc` | `name_desc`)
- **Validation (`400 Bad Request`):** Returns RFC 9457 `ValidationProblemDetails` (`application/problem+json`).
- **Response (`200 OK`):** `CatalogProductsResponse` (`Page`, `PageSize`, `TotalCount`, `TotalPages`, `Items[]` with `Price` + `CurrencyCode`, `PrimaryImageUrl`, `IsAvailable`).
- Excludes `Draft`, `Archived`, and inactive-category products; uses `long` offset arithmetic (`(long)(page - 1) * pageSize`) to prevent overflow on large `page` values.

### `GET /api/stores/{slug}/catalog/products/{productId:guid}`
- **Response (`200 OK`):** `CatalogProductDetailsDto` (includes `Description`, `Sku`, `Price`, `CurrencyCode`, `IsAvailable`, ordered `Images[]`).
- **Not Found (`404`):** Returned if the product does not exist in the resolved store, is `Draft`/`Archived`, or belongs to an inactive category.

## 3. Operational Health Probes
- `GET /health/live`: Process liveness only (`Predicate = _ => false`), `200 Healthy`.
- `GET /health/ready`: Runs `DatabaseReadinessHealthCheck` (tag `"ready"`), `200 Healthy` when SQL Server is reachable, `503 Unhealthy` otherwise without leaking connection strings.
