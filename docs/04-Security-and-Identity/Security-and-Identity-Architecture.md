# Security & Identity Architecture

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Current State (As-Is) in Production (`src/RetailFlow.Api`)
- **Authentication / Authorization:** Intentionally **not configured** (`UseAuthentication`, `UseAuthorization`, and ASP.NET Core Identity are absent). Only unauthenticated public catalog read endpoints and health probes exist.
- **Tenant Isolation:**
  - `StoreResolutionEndpointFilter` resolves `{slug}` from the route via `IStoreDirectoryResolver.ResolveActiveStoreAsync` (`Status == StoreStatus.Active` only).
  - `StoreRequestContextAccessor` is `Scoped` and uses `Interlocked.CompareExchange` to prevent overwriting tenant context mid-request.
  - Client-supplied `StoreId` is never accepted; every query in `PublicCatalogQueryService` filters by `store.StoreId` and SQL composite FKs enforce tenant boundaries at the database layer.
- **Host Hardening:**
  - `HostFilteringAllowedHostsValidator` fails startup outside Development if `AllowedHosts` is missing, empty, `*`, `0.0.0.0`, `[::]`, or starts with `.` / contains `*`, `/`, or whitespace.
  - `UseExceptionHandler()` + `AddProblemDetails()` suppress stack traces and connection strings outside Development.
  - `DatabaseReadinessHealthCheck` catches connectivity exceptions without exposing secrets while rethrowing `OperationCanceledException` on cancellation.

## 2. Experimental PoCs (Isolated in `experiments/`)
- **`experiments/IdentitySpike/`:** Demonstrates `ScopedUserStore` (`UserOnlyStore<ApplicationUser, IdentitySpikeDbContext>`) isolating `StoreCustomer` (`AccountKind = 2`, `StoreId != NULL`) and `BackOffice` (`AccountKind = 1`, `StoreId == NULL`), with `CK_AspNetUsers_AccountKind_StoreId` and `UX_AspNetUsers_StoreId_NormalizedEmail`.
- **`experiments/SessionTransportSpike/`:** Demonstrates Same-Origin cookie authentication (`StoreCustomerCookieManager`), `SqlServerTicketStore` (`ITicketStore` with `Idle 15m` / `Absolute 8h` expiration and `SecurityStamp` revocation), `BrowserCsrfProtectionMiddleware` (`X-RetailFlow-Request: 1` + strict `Origin`/`Referer` + `Sec-Fetch-Site`), `PersistKeysToDbContext<SessionTicketDbContext>` Data Protection key ring, and Playwright multi-tab cross-store isolation tests.

## 3. Target State (To-Be) & Gaps
- **Status of `AUTH-ADR-01`:** Remains `Proposed` (see [AUTH-ADR-01-Session-Transport.md](../AUTH-ADR-01-Session-Transport.md)).
- **Gaps before Production Auth Activation:**
  - Performance benchmarking of auth/session ticket store.
  - Multi-store browser context policy sign-off.
  - Production Data Protection certificate rotation & key backup runbooks.
  - Azure App Service live `ForwardedHeaders` header sanitization verification.
  - Rate limiting on future auth endpoints (`Register`/`Login`/`ForgotPassword`).
