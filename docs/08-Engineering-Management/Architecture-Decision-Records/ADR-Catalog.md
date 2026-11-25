# Architecture Decision Records (ADR Catalog)

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## ADR-001 — Modular Monolith with Clean Layering
- **Status:** Accepted (Implemented in `RetailFlow.slnx`)
- **Context & Problem:** RetailFlow needs clear module and domain boundaries without microservice operational overhead in MVP.
- **Decision:** Single deployable ASP.NET Core host (`RetailFlow.Api`) backed by `RetailFlow.Domain` -> `RetailFlow.Application` -> `RetailFlow.Infrastructure`.
- **Consequences:** Zero circular dependencies; easy local/CI testing; requires discipline to keep module boundaries clean as Cart/Orders are added.
- **Evidence:** [RetailFlow.slnx](../../../RetailFlow.slnx), `.csproj` files in `src/`.

## ADR-002 — Shared Database, Shared Schema with `StoreId` & SQL Composite Foreign Keys (`MT-01` / `REL-01`)
- **Status:** Accepted for Catalog Slice (Implemented in `RetailFlowDbContext`)
- **Context & Problem:** Multi-tenant SaaS stores must be strictly isolated without per-tenant database provisioning overhead in MVP.
- **Decision:** Shared SQL Server database (`RetailFlowProduct`) with `StoreId` on tenant-owned tables, server-side slug resolution (`StoreResolutionEndpointFilter`), explicit `StoreId` filtering in queries, and SQL composite foreign keys `(StoreId, CategoryId)` and `(StoreId, ProductId)` (`ON DELETE NO ACTION`).
- **Security & Operational Implications:** Prevents cross-tenant links even if application code is bypassed; requires every new tenant-owned table to follow the same composite FK pattern (per OWASP Multi-Tenant Security Cheat Sheet & EF Core Alternate/Composite Keys docs).
- **Evidence:** `src/RetailFlow.Infrastructure/Persistence/Configurations/*.cs`, `20261125123923_InitialCatalogSlice.cs`.

## ADR-003 — Global ISO 4217 Currency Catalog, 1:1 `StoreSettings`, and `MoneyPrecision` (`MNY-01`)
- **Status:** Accepted for Catalog Slice (Implemented; Phase 05 baseline document remains `Proposed` overall)
- **Context & Problem:** Different currencies have different decimal scales (`JPY` 0, `USD` 2, `JOD`/`KWD` 3); free-form currency strings or `Math.Round` risk silent financial corruption.
- **Decision:** Global `Currencies` table (PK `Code`, unique `NumericCode`, binary collation uppercase `Code` check, `MinorUnit 0..4`), 1:1 `StoreSettings`, `decimal(19,4)` SQL storage, and `Product.Create` binding `StoreSettings` + `Currency` via `MoneyPrecision.EnsureValidAmount`.
- **Evidence:** `Currency.cs`, `MoneyPrecision.cs`, `StoreSettings.cs`, `Product.cs`, `CurrencyConfiguration.cs`.

## ADR-004 — Same-Origin Hosting on Azure App Service Linux + Built-in .NET 10 Runtime
- **Status:** Accepted for Hosting Topology (Host prepared; Azure provisioning deferred)
- **Context & Problem:** Minimize deployment complexity and avoid CORS/cross-origin cookie pitfalls.
- **Decision:** Deploy framework-dependent .NET 10 Web API on Azure App Service Linux (`DOTNETCORE:10.0`) with Same-Origin SPA fallback in `wwwroot`, `UseForwardedHeaders`, `AllowedHosts` startup validation, and future GitHub Actions OIDC deployment.
- **Consequences:** Requires verifying live `ForwardedHeaders` behavior on Azure App Service before production launch.
- **Evidence:** `ProductHostExtensions.cs`, [product-host-bootstrap.md](../../product-host-bootstrap.md).

## AUTH-ADR-01 — Session Transport Strategy
- **Status:** **Proposed** (v0.14 — NOT activated in `RetailFlow.Api`)
- **Reference:** [AUTH-ADR-01-Session-Transport.md](../../AUTH-ADR-01-Session-Transport.md)
