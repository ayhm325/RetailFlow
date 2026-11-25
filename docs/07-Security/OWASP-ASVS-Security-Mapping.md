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

## 2. Authentication, Session Transport & Access Control (`AUTH-ADR-01` Accepted)

| Standard Mapping | Control & Implementation | Verified Evidence (Tests & Code) | Status |
|---|---|---|---|
| **V2 / V3 Auth & Session Management** (`AUTH-ADR-01`, `OD-006`) | `__Host-RetailFlow.Session` (`HttpOnly`, `SameSite=Lax`, `SecurePolicy=Always`), SQL-backed `SqlSessionTicketStore : ITicketStore`, PBKDF2-SHA256 (310,000 iterations, constant-time compare), `AppUsers` tenant-scoped unique email. | `AuthEndpoints.cs`, `SqlSessionTicketStore.cs`, `PasswordHasherService.cs`, `EndToEndCommerceAuthIntegrationTests` | **Implemented & Verified** |
| **V4.2 / A01 CSRF Protection** (`REF-06`) | Unsafe HTTP methods (`POST`/`PUT`/`DELETE`/`PATCH`) for cookie-authenticated requests require `X-CSRF: 1` header; missing header returns `403 ProblemDetails`. | `ProductHostExtensions.cs`, `UnauthenticatedAndCsrfMissingRequests_AreRejectedWith401And403` | **Implemented & Verified** |
| **V4.1 / API1:2023 Access Control** (`NFR-001`) | Role policies (`Customer`, `MerchantOwner`, `PlatformAdmin`) + `TenantClaimMatchEndpointFilter` verifying `"store_id"` claim matches resolved `{slug}` store. | `MerchantEndpoints.cs`, `CustomerOrderEndpoints.cs`, `PlatformAdminEndpoints.cs`, `FullCommerceFlow_Auth_Bola_Idempotency_Od002Cancel_And_Od001Expiry_AllVerified` | **Implemented & Verified** |
