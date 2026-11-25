# Deployment & Configuration

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Current State (As-Is) — CI Pipeline
Defined in [.github/workflows/ci.yml](../../.github/workflows/ci.yml):
- **Triggers:** `push` (`main`, `feature/**`), `pull_request` (`main`), `workflow_dispatch`.
- **Runner & Services:** `ubuntu-24.04` with `mcr.microsoft.com/mssql/server:2022-latest` health-checked service.
- **Databases:**
  - `RetailFlowProduct` (`RETAILFLOW_CONNECTION` / `ConnectionStrings__RetailFlow`) — runs `RetailFlowDbContext` migrations (`InitialCatalogSlice`, `HardenCurrencyConstraints`).
  - `RetailFlowIdentitySpike` (`RETAILFLOW_IDENTITY_SPIKE_CONNECTION`) — runs `IdentitySpikeDbContext` and `SessionTicketDbContext` migrations.
- **Execution:** Builds and tests all 4 solutions in `Release`, installs Playwright Chromium, and uploads `.trx` and Playwright artifacts (`ci-test-diagnostics`).

## 2. Connection String Resolution ([ConnectionStringResolver.cs](../../src/RetailFlow.Infrastructure/Persistence/ConnectionStringResolver.cs))
- **Runtime (`ResolveRuntime`):**
  1. `configuration["RETAILFLOW_CONNECTION"]` (if non-whitespace) — read via `IConfiguration` so host/test overrides take precedence over process env vars.
  2. `configuration.GetConnectionString("RetailFlow")` (if non-whitespace).
  3. Local SQL Server fallback **only** when `isDevelopment == true`; otherwise fails fast on startup via `RetailFlowDatabaseOptionsValidator` (`ValidateOnStart()`) without logging secrets.
- **Design-Time (`ResolveDesignTime`):** Used by `RetailFlowDbContextFactory` for `dotnet ef` CLI commands (`RETAILFLOW_CONNECTION` -> `ConnectionStrings__RetailFlow` -> `DefaultLocalConnectionString`).

## 3. Target State (To-Be) — Azure App Service on Linux
- **Approved Hosting Decisions:** Azure App Service on Linux, Built-in .NET 10 LTS Runtime (`DOTNETCORE:10.0`, framework-dependent), Same-Origin SPA + API, GitHub Actions + OIDC (`id-token: write` deferred to future deploy workflow).
- **Required Production Settings (not committed in `appsettings.json`):** `ASPNETCORE_ENVIRONMENT=Production`, `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`, `AllowedHosts=<hostname>`, `RETAILFLOW_CONNECTION` or `ConnectionStrings__RetailFlow`, and **HTTPS Only** enabled.
- **Gap:** Azure infrastructure provisioning, deploy workflow, and live Staging `ForwardedHeaders` verification are not yet implemented.
