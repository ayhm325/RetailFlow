# RetailFlow — OWASP ASVS & Security Controls Mapping (`AUD-009`)

**Last Verified:** `2026-11-26` | **Branch:** `feature/public-store-catalog-read`
**References:** OWASP ASVS v4.0.3 (`REF-11`), OWASP Multi-Tenant Security Cheat Sheet (`REF-05`), OWASP CSRF Prevention Cheat Sheet (`REF-06`), RFC 9457 (`REF-07`), Microsoft Learn ASP.NET Core 10 (`REF-01`, `REF-02`, `REF-09`).

---

## 1. Implemented Product Host Security Controls

| ASVS / OWASP Area | Control & Implementation | Verified Evidence (Tests & Code) | Status |
|---|---|---|---|
| **V1.4 / Multi-Tenant Isolation** (`REF-05`) | Server-side store resolution via `{slug}` in `StoreResolutionEndpointFilter` (`Status == Active`); composite tenant FKs `(StoreId, CategoryId)` / `(StoreId, ProductId)`; all `IMerchantStoreService` & `IMerchantCatalogService` queries scoped by `storeId`. | `StoreResolutionEndpointFilter.cs`, `PublicStoreCatalogIntegrationTests`: `Products_ExcludeDraftArchivedInactiveCategoryAndOtherStoreProducts_AndReactivateCleanly`, `MerchantServices_EnforceTenantIsolationAndUpdateCatalogAndDeliveryZones` | **Implemented & Verified** |
| **V1.6 / Data Minimization & Privacy** (`BR-011`, `ST-04`) | Internal `StoreSettings.FulfillmentAddress` excluded from `PublicStoreIdentityDto`; `OnlineOnly` stores project `PublicAddress = null` and `PickupEnabled = false`. | `PublicCatalogQueryService.cs`, `PublicStoreIdentity_ReturnsBrandingAndPublicAddress_AndNeverExposesFulfillmentAddress` | **Implemented & Verified** |
| **V5.1 / Input Validation & Parameterized Queries** | `CatalogProductsQuery` clamps/validates `page >= 1`, `pageSize 1..50`, `search <= 100` with `ValidationProblemDetails`; EF Core parameterized queries; `long` pagination offset prevents integer overflow. | `SearchOverMaxLength_Returns400ValidationProblemDetails`, `Products_LargePageNumber_DoesNotOverflowOffset` | **Implemented & Verified** |
| **V7.4 / Error Handling & Secret Hygiene** (`REF-07`, `AUD-015`) | Non-Development `UseExceptionHandler()` returns RFC 9457 `ProblemDetails` without stack traces; `/health/ready` never leaks connection strings; CI uses ephemeral masked SQL SA password. | `ProductionHost_CatalogErrorsDoNotLeakInternalDetails`, `HealthEndpoints_LiveIsDbIndependent_AndReadyChecksSqlWithoutLeakingConnectionStrings`, `.github/workflows/ci.yml` | **Implemented & Verified** |
| **V14.4 / Host Header & Proxy Hardening** (`REF-02`) | Startup `HostFilteringAllowedHostsValidator` rejects `*`, `0.0.0.0`, `[::]`, leading `.`, and empty `AllowedHosts` in non-Development; `UseForwardedHeaders()` runs before `UseHsts()` and `UseHttpsRedirection()`. | `ProductHostIntegrationTests` (`AllowedHosts_*`, `ForwardedHeaders_*`) | **Implemented & Verified** |

---

## 2. Target / PoC Security Controls (Pending `AUTH-ADR-01` Acceptance)

| ASVS / OWASP Area | Target / PoC Control | Current Status |
|---|---|---|
| **V2 / V3 Authentication & Session Management** (`AUTH-ADR-01`, `OD-006`) | Cookie + server-side `ITicketStore` + Data Protection key ring + `UserStore` composite `(StoreId, NormalizedUserName/Email)`. | **PoC Verified (`IdentitySpike`, `SessionTransportSpike`) — Not Activated in Product Host** |
| **V4.2 CSRF Protection** (`REF-06`) | `SameSite=Lax`, `X-CSRF` / anti-forgery token, and `Origin`/`Referer` validation for Same-Origin unsafe HTTP methods. | **PoC Verified (`BrowserCsrfProtectionMiddleware`) — Pending Product Host Auth Integration** |
| **V4.1 Access Control** (`NFR-001`) | Role & `StoreId` claim authorization on Merchant/Admin/Customer HTTP endpoints. | **Pending `AUTH-ADR-01` Approval** (no unauthenticated write HTTP endpoints exposed) |
