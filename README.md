# RetailFlow

RetailFlow is a multi-tenant e-commerce SaaS platform built with **.NET 10 LTS** (`10.0.404` SDK / `10.0.15` packages), **ASP.NET Core Web API**, **EF Core**, and **SQL Server** using a **Modular Monolith** architecture (`Shared Database + Shared Schema + StoreId`).

- **Documentation Entry Point:** [docs/README.md](docs/README.md)
- **Engineering Operating Memory & Control State:** [AGENTS.md](AGENTS.md) | [docs/engineering/README.md](docs/engineering/README.md)
- **Production Solution:** [RetailFlow.slnx](RetailFlow.slnx)
- **CI Workflow:** [.github/workflows/ci.yml](.github/workflows/ci.yml)

## Repository Structure

```text
RetailFlow/
├── src/                         # Production Modular Monolith (Api, Application, Domain, Infrastructure)
├── tests/                       # Product Host integration tests (RetailFlow.Api.IntegrationTests)
├── experiments/                 # Isolated PoCs (IdentitySpike, SessionTransportSpike) — not referenced by src/
├── docs/
│   ├── 00-Project-Overview/     # Product vision, actors, and glossary
│   ├── 01-Requirements/         # Canonical Gate 1 registers (AC, NFR, BR, Use Cases)
│   ├── 02-Architecture/         # System architecture & Mermaid diagrams
│   ├── 03-Database/             # SQL Server schema, composite FKs & migrations
│   ├── 04-Security-and-Identity/# Production tenant isolation vs PoC boundaries
│   ├── 05-API/                  # Public Catalog API & ProblemDetails contracts
│   ├── 06-Quality-and-Testing/  # Test suites & SQL Server verification rules
│   ├── 07-Deployment-and-Operations/ # Health probes, ConnectionStringResolver & Azure target
│   ├── 08-Engineering-Management/    # Roadmap, ADRs, Change Log & Open Decisions
│   ├── 09-Audit-and-Traceability/    # Audit reports, Gate 1 reports & FR traceability matrix
│   └── engineering/             # AI Operating Memory (control/, registers/, baseline/, audit/)
├── scripts/                     # Gate 1 baseline validator (validate-gate1-baseline.ps1)
├── tools/                       # Operating memory validator (validate_operating_memory.py)
├── .agents/                     # Antigravity project rules & skills
├── .github/                     # CI workflows & agent prompts
├── AGENTS.md / CLAUDE.md / GEMINI.md
├── RetailFlow.slnx
└── global.json
```

## Current Implementation Status (Product Host verified at `04392c875c275e99f4b5ee378f63b3a8b8fc541d`)

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
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-gate1-baseline.ps1
python tools/validate_operating_memory.py
```
