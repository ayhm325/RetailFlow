# تشغيل محلي للمطور

## المتطلبات

- .NET SDK 10.0.404 feature band أو SDK متوافق مع `global.json` (`rollForward: latestPatch`).
- SQL Server متاح محلياً أو container؛ التكامل يستخدم SQL Server features وليس EF InMemory.
- Git.
- dotnet-ef tool متوافق مع EF Core 10.0.15 عند تشغيل migrations.

## إعداد الاتصال

PowerShell:

```powershell
$env:RETAILFLOW_CONNECTION = "Server=localhost;Database=RetailFlowProduct;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True"
```

أو استخدم `ConnectionStrings__RetailFlow`. لا تستخدم بيانات اتصال حقيقية في scripts committed.

## Restore / Build / Run

```powershell
dotnet restore RetailFlow.slnx
dotnet build RetailFlow.slnx --configuration Debug
dotnet run --project src/RetailFlow.Api/RetailFlow.Api.csproj
```

Launch profile يحدد `https://localhost:56062` و`http://localhost:56063` وDevelopment environment.

## OpenAPI وHealth

في Development:

- OpenAPI: `/openapi/v1.json` (حسب MapOpenApi convention).
- Liveness: `/health/live`.
- Readiness: `/health/ready`.

## الاختبارات

```powershell
dotnet test RetailFlow.slnx --configuration Release
```

شغّل SQL Server قبل integration tests، واضبط connection string للقاعدة المناسبة.

## Identity/Session experiments

هذه ليست ضرورية لتشغيل Product Host. لها solutions مستقلة تحت `experiments/`. CI يبنيها ويشغل اختباراتها منفصلة. لا تخلط connection string الخاصة بالتجربة مع Product Host.

## ملاحظات

- لا يوجد React/TypeScript client في هذا commit.
- قد يعمل SPA fallback فقط عند نشر `wwwroot/index.html`، لكن هذا لا يخلق Frontend source داخل repo.
- لا تبدأ Production mode محلياً دون ضبط `AllowedHosts` صريح وconnection string.
