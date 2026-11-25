---
trigger: glob
globs: "**/*.cs, **/*.csproj, **/*.sln, **/*.slnx, **/*.json"
description: Apply when changing or reviewing .NET/C# projects, DI, middleware, API endpoints, EF Core, configuration, or backend tests.
---
# .NET backend
- Inspect target framework, SDK pin, package versions, nullable settings, analyzers, and existing conventions first.
- Keep Domain independent of infrastructure/framework concerns; Application owns use cases/contracts; Infrastructure implements persistence/external adapters; API composes and exposes HTTP.
- Do not inject DbContext into endpoints when the existing application boundary provides a service/handler.
- Respect async cancellation, explicit transaction boundaries, concurrency tokens, constraints, and deterministic query behavior.
- Use ProblemDetails consistently; do not leak exception details or secrets.
- Migrations must be reviewed for forward/backward compatibility, data loss, locks, and rollback/forward-fix strategy.
- Add tests for success, validation, authorization/tenant isolation, concurrency, and failure behavior where relevant.
