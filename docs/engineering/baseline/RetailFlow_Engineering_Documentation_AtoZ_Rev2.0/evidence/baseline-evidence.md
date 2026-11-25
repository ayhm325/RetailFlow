# RetailFlow baseline evidence

## Repository snapshot
- Repository: https://github.com/ayhm325/RetailFlow
- Commit: `04392c875c275e99f4b5ee378f63b3a8b8fc541d`
- Commit date shown by GitHub: 2026-11-25 13:23:47 UTC
- Branch `main` pointed to this commit when queried.
- GitHub commit status queried: `pending`, no status entries returned. This is not a CI pass statement.
- SDK pin: `global.json` -> `10.0.404`.

## Observed Product Host structure
- `src/RetailFlow.Api`
- `src/RetailFlow.Application`
- `src/RetailFlow.Domain`
- `src/RetailFlow.Infrastructure`
- `tests/RetailFlow.Api.IntegrationTests`
- Root solution: `RetailFlow.slnx`

## Observed current Product Host routes
- `GET /api/stores/{slug}/catalog/categories`
- `GET /api/stores/{slug}/catalog/products`
- `GET /api/stores/{slug}/catalog/products/{productId:guid}`

## Observed implementation source paths
- `src/RetailFlow.Api/Program.cs`
- `src/RetailFlow.Api/Hosting/ProductHostExtensions.cs`
- `src/RetailFlow.Api/Hosting/DatabaseReadinessHealthCheck.cs`
- `src/RetailFlow.Api/Catalog/PublicCatalogEndpoints.cs`
- `src/RetailFlow.Api/Catalog/StoreResolutionEndpointFilter.cs`
- `src/RetailFlow.Application/Catalog/CatalogContracts.cs`
- `src/RetailFlow.Application/Stores/StoreRequestContext.cs`
- `src/RetailFlow.Domain/Stores/Store.cs` and `StoreSettings.cs`
- `src/RetailFlow.Domain/Catalog/Product.cs`, `Category.cs`, `ProductStatus.cs`
- `src/RetailFlow.Domain/Inventory/InventoryItem.cs`
- `src/RetailFlow.Infrastructure/Persistence/RetailFlowDbContext.cs`
- `src/RetailFlow.Infrastructure/Persistence/Configurations/*`
- `src/RetailFlow.Infrastructure/Catalog/PublicCatalogQueryService.cs`
- `src/RetailFlow.Infrastructure/Stores/StoreDirectoryResolver.cs`
- `src/RetailFlow.Infrastructure/Persistence/Migrations/20261125123923_InitialCatalogSlice.cs`
- `src/RetailFlow.Infrastructure/Persistence/Migrations/20261125130511_HardenCurrencyConstraints.cs`
- `tests/RetailFlow.Api.IntegrationTests/PublicStoreCatalogIntegrationTests.cs`
- `.github/workflows/ci.yml`

## Explicit absences at this snapshot
No `ClientApp`/React app in the repository tree; Product Host does not configure ASP.NET Core Identity Authentication/Authorization; no merchant catalog writes; no customer profile/address API; no cart, checkout, order, reservation lifecycle, delivery zone, payment gateway, or production Azure deployment workflow.

## Experiments are not Product Host integration
- `experiments/IdentitySpike` and `experiments/SessionTransportSpike` are separate projects.
- `docs/AUTH-ADR-01-Session-Transport.md` marks session decision Proposed, despite PoC test evidence.
- PoC counts are documented project evidence, not a fresh run conducted by this documentation task.

## Review limitations
This review inspected the Git tree and selected files, but did not execute build/test commands, provision Azure resources, run a security penetration test, or verify a live production deployment. Re-run build/test/scan/deployment evidence before release approval.
