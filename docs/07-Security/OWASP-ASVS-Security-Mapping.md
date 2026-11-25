# RetailFlow — OWASP Top 10, API Security Top 10 & ASVS Mapping (`AUD-009`)

**Last Verified:** `2026-11-26` | **Branch:** `feature/public-store-catalog-read`
**References:** OWASP Top 10 (`REF-13`), OWASP API Security Top 10 (`REF-14`), OWASP ASVS v4.0.3 (`REF-11`), OWASP Multi-Tenant Security (`REF-05`), OWASP CSRF Prevention (`REF-06`), RFC 9457 (`REF-07`), Microsoft Learn ASP.NET Core 10 (`REF-01`, `REF-02`, `REF-09`, `REF-15`).

---

## 1. Implemented Product Host Security Controls (Verifiable & Tested)

| Standard Mapping | Control & Implementation | Verified Evidence (Tests & Code) | Status |
|---|---|---|---|
| **API1:2023 (BOLA) / A01:2021 / ASVS V1.4** | Server-side tenant resolution via `{slug}` (`Status == Active`); composite FKs `(StoreId, CategoryId)` & `(StoreId, ProductId)`; all `IMerchantStoreService`, `IMerchantCatalogService`, and `IPlatformStoreService` queries scoped by `storeId`. | `API1_BOLA_CrossStoreProductIdAndCategoryFilter_AreBlocked`, `MerchantServices_EnforceTenantIsolationAndUpdateCatalogAndDeliveryZones`, `PlatformStoreLifecycle_AndProductImages_EnforceVisibilityAndTenantIsolation` | **Implemented & Verified** |
| **API3:2023 (BOPLA) / ASVS V1.6** | Internal `StoreSettings.FulfillmentAddress` excluded from `PublicStoreIdentityDto`; `OnlineOnly` stores project `PublicAddress = null` and `PickupEnabled = false`. | `PublicStoreIdentity_ReturnsBrandingAndPublicAddress_AndNeverExposesFulfillmentAddress` | **Implemented & Verified** |
| **API4:2023 (Resource Consumption)** | Partitioned Fixed-Window Rate Limiter (`AddRateLimiter` / `UseRateLimiter`) returning `429` RFC 9457 `ProblemDetails` (with `/health/*` exempt); `CatalogProductsQuery` clamps `pageSize <= 50` and `search <= 100`. | `API4_RateLimiting_ExceedingLimit_Returns429ProblemDetails_AndHealthIsExempt`, `SearchOverMaxLength_Returns400ValidationProblemDetails` (`REF-15`) | **Implemented & Verified** |
| **A03:2021 (Injection) / ASVS V5.1** | EF Core parameterized LINQ queries; `long` offset pagination arithmetic prevents overflow; `Latin1_General_BIN2` & SQL `CHECK` constraints enforce domain invariants in DB. | `Products_LargePageNumber_DoesNotOverflowOffset`, `CurrencyAndStoreSettings_SqlConstraintsAndPrecision_EnforcedInDatabase` | **Implemented & Verified** |
| **A05:2021 / API8:2023 / ASVS V14.4** | Security headers (`X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`), startup `AllowedHosts` validation, `UseForwardedHeaders()` before `HSTS`/`HttpsRedirection`, `ProblemDetails` error hiding, and ephemeral CI SQL passwords (`AUD-015`). | `A05_SecurityHeaders_ArePresentOnApiResponses`, `ProductHostIntegrationTests`, `ProductionHost_CatalogErrorsDoNotLeakInternalDetails`, `.github/workflows/ci.yml` | **Implemented & Verified** |
| **API9:2023 (Improper Inventory Management)** | Explicit OpenAPI `/openapi/v1.json` (Development-only) documenting all 5 public endpoints + `400`/`404` `ProblemDetails` responses (`AUD-014`). | `OpenApiDocument_InDevelopment_IncludesPublicEndpointsAndProblemResponses` | **Implemented & Verified** |

---

## 2. Target / PoC Security Controls (Pending `AUTH-ADR-01` Acceptance)

| ASVS / OWASP Area | Target / PoC Control | Current Status |
|---|---|---|
| **V2 / V3 Authentication & Session Management** (`AUTH-ADR-01`, `OD-006`) | Cookie + server-side `ITicketStore` + Data Protection key ring + `UserStore` composite `(StoreId, NormalizedUserName/Email)`. | **PoC Verified (`IdentitySpike`, `SessionTransportSpike`) — Not Activated in Product Host** |
| **V4.2 CSRF Protection** (`REF-06`) | `SameSite=Lax`, `X-CSRF` / anti-forgery token, and `Origin`/`Referer` validation for Same-Origin unsafe HTTP methods. | **PoC Verified (`BrowserCsrfProtectionMiddleware`) — Pending Product Host Auth Integration** |
| **V4.1 Access Control** (`NFR-001`) | Role & `StoreId` claim authorization on Merchant/Admin/Customer HTTP endpoints. | **Pending `AUTH-ADR-01` Approval** (no unauthenticated write HTTP endpoints exposed) |
