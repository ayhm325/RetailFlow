# RetailFlow

RetailFlow is a multi-tenant e-commerce SaaS platform built with **.NET 10 LTS** (`10.0.404` SDK / `10.0.15` packages), **ASP.NET Core Web API**, **EF Core**, and **SQL Server** using a **Modular Monolith** architecture (`Shared Database + Shared Schema + StoreId`).

- **Documentation Entry Point:** [docs/README.md](docs/README.md)
- **Production Solution:** [RetailFlow.slnx](RetailFlow.slnx)
- **CI Workflow:** [.github/workflows/ci.yml](.github/workflows/ci.yml)

## Current Implementation Status (Verified at `04392c875c275e99f4b5ee378f63b3a8b8fc541d`)

- **Production (`src/` + `tests/`):**
  - Product Host foundation (`RetailFlow.Api`): `ForwardedHeaders`, `AllowedHosts` startup validation, `ProblemDetails`, conditional Same-Origin SPA fallback, `/health/live`, and `/health/ready` (`DatabaseReadinessHealthCheck`).
  - Vertical Slice 01 — Public Store Catalog Read API (`RetailFlow.Domain`, `RetailFlow.Application`, `RetailFlow.Infrastructure`, `RetailFlow.Api`): `Currency` ISO 4217 catalog, `Store`, `StoreSettings`, `Category`, `Product`, `ProductImage`, `InventoryItem`, `MoneyPrecision`, tenant-isolated SQL composite foreign keys, and 3 public catalog endpoints.
- **Experimental PoCs (`experiments/` — isolated from production):**
  - `experiments/IdentitySpike/`: Tenant-scoped ASP.NET Core Identity `ScopedUserStore` (`AccountKind` + `StoreId`).
  - `experiments/SessionTransportSpike/`: Same-Origin cookie transport, `SqlServerTicketStore`, `BrowserCsrfProtectionMiddleware`, Data Protection key ring, and Playwright E2E tests.
- **Not Yet Activated in Production:** ASP.NET Core Identity, Authentication/Authorization (`AUTH-ADR-01` remains `Proposed`), React frontend, Cart/Checkout/Orders, and Azure deployment workflows.

## Quick Verification Commands

```powershell
dotnet build RetailFlow.slnx --configuration Release
dotnet test RetailFlow.slnx --configuration Release --no-build
```
