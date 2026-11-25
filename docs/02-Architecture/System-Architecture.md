# System Architecture

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Current State (As-Is) vs. Target State (To-Be)
- **As-Is:** Production solution (`RetailFlow.slnx`) is a Clean Architecture / Modular Monolith with 4 `src/` layers (`Domain`, `Application`, `Infrastructure`, `Api`) and 1 `tests/` project (`RetailFlow.Api.IntegrationTests`). Two experimental PoCs (`experiments/IdentitySpike/`, `experiments/SessionTransportSpike/`) remain completely isolated.
- **To-Be:** Add Merchant/Admin, Cart, Checkout/Orders, Fulfillment, React SPA (`wwwroot`), and integrated Identity/Session Transport once `AUTH-ADR-01` acceptance gates close.
- **Gap:** No authentication/authorization, no write endpoints, and no React frontend in `src/`.

## 2. Solution & Project Dependency Graph

```mermaid
flowchart TD
    Api["RetailFlow.Api (Composition Root & HTTP)"] --> App["RetailFlow.Application (Contracts & DTOs)"]
    Api --> Infra["RetailFlow.Infrastructure (EF Core & SQL Server)"]
    Infra --> App
    Infra --> Domain["RetailFlow.Domain (Entities & Invariants)"]
    App --> Domain
    Tests["RetailFlow.Api.IntegrationTests"] --> Api
    Tests --> Infra

    IdSpike["experiments/IdentitySpike (PoC - Isolated)"] -. "No reference" .- Api
    SessSpike["experiments/SessionTransportSpike (PoC - Isolated)"] --> IdSpike
```

- `RetailFlow.Domain` has zero NuGet or project dependencies.
- `RetailFlow.Application` references `RetailFlow.Domain` only (no EF Core, SQL Server, or ASP.NET Core packages).
- `RetailFlow.Infrastructure` implements `IStoreDirectoryResolver`, `IStoreRequestContextAccessor`, `IPublicCatalogQueryService`, and `RetailFlowDbContext`.
- `RetailFlow.Api` composes host middleware (`ProductHostExtensions`) and catalog endpoints (`PublicCatalogEndpoints`).

## 3. Domain & Database Entity Relationships (As-Is)

```mermaid
erDiagram
    Currencies ||--o{ StoreSettings : "FK CurrencyCode (Restrict)"
    Stores ||--|| StoreSettings : "1:1 StoreId (UX_StoreSettings_StoreId)"
    Stores ||--o{ Categories : "StoreId (Restrict)"
    Stores ||--o{ Products : "StoreId (Restrict)"
    Categories ||--o{ Products : "Composite FK (StoreId, CategoryId)"
    Products ||--o{ ProductImages : "Composite FK (StoreId, ProductId)"
    Products ||--o| InventoryItems : "Composite FK (StoreId, ProductId)"
```

## 4. Public Catalog Request & Tenant Isolation Lifecycle

```mermaid
sequenceDiagram
    participant C as Client
    participant F as StoreResolutionEndpointFilter
    participant R as StoreDirectoryResolver
    participant A as StoreRequestContextAccessor (Scoped)
    participant Q as PublicCatalogQueryService
    participant DB as SQL Server (RetailFlowProduct)

    C->>F: GET /api/stores/{slug}/catalog/...
    F->>R: ResolveActiveStoreAsync(slug)
    R->>DB: SELECT Store + StoreSettings + Currency WHERE Slug = @slug AND Status = Active
    alt Store missing or inactive
        F-->>C: 404 ProblemDetails
    else Active store found
        F->>A: SetCurrent(StoreRequestContext)
        F->>Q: GetCategories / GetProducts / GetProductDetails
        Q->>DB: AsNoTracking() SQL projection WHERE StoreId = @storeId
        Q-->>C: 200 OK (DTO) / 404 ProblemDetails
    end
```

## 5. Deployment Architecture (As-Is CI vs. To-Be Azure)

```mermaid
flowchart LR
    Dev["Developer / GitHub PR"] --> GHA["GitHub Actions CI (ubuntu-24.04)"]
    GHA --> SQLCI["SQL Server 2022 Service (RetailFlowProduct + RetailFlowIdentitySpike)"]
    GHA -. "Future OIDC Deploy (Not Implemented)" .-> Azure["Azure App Service Linux (.NET 10 Built-in Runtime)"]
    Azure -. "Same-Origin TLS Termination" .-> App["RetailFlow.Api (Kestrel + ForwardedHeaders)"]
```
