# RetailFlow Product API Host — Bootstrap Documentation

- **Solution:** [`RetailFlow.slnx`](../RetailFlow.slnx)
- **Host Project:** [`src/RetailFlow.Api/RetailFlow.Api.csproj`](../src/RetailFlow.Api/RetailFlow.Api.csproj) (`.NET 10`, `Microsoft.NET.Sdk.Web`)
- **Integration Tests:** [`tests/RetailFlow.Api.IntegrationTests/RetailFlow.Api.IntegrationTests.csproj`](../tests/RetailFlow.Api.IntegrationTests/RetailFlow.Api.IntegrationTests.csproj)

---

## 1. Host Responsibilities

`RetailFlow.Api` is the clean production Host foundation for RetailFlow, completely isolated from `experiments/IdentitySpike/` and `experiments/SessionTransportSpike/`.

Current responsibilities:
1. Application bootstrap using the .NET 10 minimal hosting model ([`Program.cs`](../src/RetailFlow.Api/Program.cs)).
2. Infrastructure DI composition via [`ProductHostExtensions.cs`](../src/RetailFlow.Api/Hosting/ProductHostExtensions.cs).
3. Centralized exception handling outside Development (`UseExceptionHandler()`) with RFC 9457 `ProblemDetails`.
4. Health endpoints (`/health/live` and `/health/ready`).
5. Forwarded Headers integration (`UseForwardedHeaders()`).
6. HTTPS Redirection and non-Development HSTS.
7. Static file hosting from `wwwroot` (`UseDefaultFiles()` + `UseStaticFiles()`) and conditional React SPA fallback.
8. Development-only OpenAPI endpoint (`/openapi/v1.json`).
9. Fail-fast startup validation for `AllowedHosts` outside Development.

---

## 2. Project & Solution Structure

- `RetailFlow.slnx`: Initially created with `src/RetailFlow.Api` and `tests/RetailFlow.Api.IntegrationTests`; subsequently expanded in Vertical Slice 01 to also include `src/RetailFlow.Domain`, `src/RetailFlow.Application`, and `src/RetailFlow.Infrastructure`. See [public-store-catalog-slice.md](public-store-catalog-slice.md).
- `experiments/IdentitySpike/` and `experiments/SessionTransportSpike/` remain independent and unreferenced.

---

## 3. HTTP Middleware Pipeline & Ordering

Implemented in `ProductHostExtensions.UseProductHostPipeline` and `MapProductHostEndpoints`:

1. **`UseForwardedHeaders()`** — Must execute before `UseHsts()` and `UseHttpsRedirection()` so the original client scheme (`X-Forwarded-Proto`) is restored before redirect/HSTS decisions.
2. **`UseExceptionHandler()` (outside Development)** — Catches unhandled exceptions and emits `ProblemDetails` without stack traces or sensitive internal values.
3. **`UseHsts()` (outside Development)** — Enforces Strict-Transport-Security headers in Staging/Production.
4. **`UseHttpsRedirection()`** — Redirects plain HTTP requests to HTTPS.
5. **`UseDefaultFiles()` & `UseStaticFiles()`** — Serves static assets (and `index.html` at `/`) from `wwwroot` when present.
6. **Development-only `MapOpenApi()`** — Exposes `/openapi/v1.json` only when `IsDevelopment()` is true.
7. **Health Endpoints (`/health/live`, `/health/ready`)** — Mapped explicitly before SPA fallback.
8. **Conditional React SPA Fallback** — Serves `wwwroot/index.html` only when it exists and only for browser navigation requests (`GET`/`HEAD`, no file extension, and typed `Accept` header negotiation explicitly accepting `text/html` with quality `q > 0`; `text/html;q=0` and `*/*`-only or malformed `Accept` values return `404`), sets `Vary: Accept`, and explicitly excludes `/api/*` and `/health/*`.

---

## 4. Health Endpoint Semantics & Limitations

- `GET /health/live`: Confirms the ASP.NET Core process and HTTP pipeline are responsive (`Predicate = _ => false`), independent of database connectivity.
- `GET /health/ready`: Filters for registrations tagged `"ready"`. In the initial bootstrap stage no `DbContext` existed; as of Vertical Slice 01, `DatabaseReadinessHealthCheck` is registered under `"ready"` and executes `RetailFlowDbContext.Database.CanConnectAsync`, returning `200 Healthy` when SQL Server is reachable and `503 Unhealthy` when unreachable.
- Neither endpoint requires authentication, and neither exposes connection strings, environment variables, internal paths, secrets, or exception details.

---

## 5. Static Files & Conditional React SPA Fallback

- Prepared for Same-Origin deployment (`/` for React SPA, `/api/*` for API, `/health/*` for health checks).
- Does **not** require `wwwroot` or `wwwroot/index.html` to start.
- Uses `context.Request.GetTypedHeaders().Accept` for content negotiation: serves `index.html` only when an explicit `text/html` media type is present with effective quality `> 0` (omitted quality defaults to `1`; any explicit `text/html;q=0` rejects HTML even if `*/*;q=1` is present, and missing/malformed `Accept` headers return `404`).
- Sets `Vary: Accept` on SPA fallback responses so caches never reuse an HTML response for a request with a different `Accept` header.
- Unknown `/api/*` and `/health/*` routes always return `404 Not Found`, never `index.html`.

---

## 6. `AllowedHosts` Startup Validation

- `appsettings.Development.json` configures `AllowedHosts` as `localhost;127.0.0.1` (Development settings file is gitignored per `.gitignore`).
- `appsettings.json` intentionally omits `AllowedHosts` so Staging/Production environments must supply an explicit hostname.
- Outside Development, `HostFilteringAllowedHostsValidator` (`IValidateOptions<HostFilteringOptions>` with `ValidateOnStart`) fails fast at startup if `HostFilteringOptions.AllowedHosts` is empty, whitespace, `*`, `0.0.0.0`, `[::]`, leading-dot wildcards (`.domain`), or contains `*`, `/`, or whitespace.

---

## 7. Azure App Service on Linux — Forwarded Headers & Required Settings

Azure App Service on Linux terminates TLS at the platform front-end and does not expose a universal stable proxy IP/CIDR for `KnownProxies`.

Required Azure App Service configuration (to be set in App Service Configuration when provisioned; **not** placed in `appsettings.json`):
- `ASPNETCORE_ENVIRONMENT=Production`
- `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`
- `AllowedHosts=<actual provisioned hostname>`
- **HTTPS Only** = enabled on App Service

**Trust implication:** Microsoft documents that `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` enables `X-Forwarded-For` and `X-Forwarded-Proto` while clearing `KnownProxies` and `KnownNetworks` restrictions. It is **not** equivalent to a restricted `KnownProxies` allowlist. Kestrel must remain isolated behind Azure App Service, and live forwarded-header sanitization must be verified after the first Azure deployment.

---

## 8. Explicitly Excluded Responsibilities (Bootstrap Baseline)

- No ASP.NET Core Identity, `UseAuthentication()`, or `UseAuthorization()` (still excluded as of Vertical Slice 01).
- In the initial bootstrap commit (`e2ad578`), no `DbContext`, database connection strings, EF Core migrations, or business domain modules existed. Those were subsequently introduced in Vertical Slice 01 (`RetailFlowDbContext`, `ConnectionStringResolver`, `InitialCatalogSlice` / `HardenCurrencyConstraints`, and Public Store Catalog endpoints; see [public-store-catalog-slice.md](public-store-catalog-slice.md)).
- No React frontend (`ClientApp`) or npm packages.
- No CORS policies.
- No Dockerfiles or Azure deployment steps.

---

## 9. Local Run, Build & Test Commands

```powershell
# Build production solution in Release
dotnet build RetailFlow.slnx --configuration Release

# Run Product Host integration tests in Release
dotnet test RetailFlow.slnx --configuration Release --no-build --logger "console;verbosity=normal"

# Run the Product Host locally (Development)
dotnet run --project src/RetailFlow.Api/RetailFlow.Api.csproj
```
