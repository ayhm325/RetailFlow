# خط أساس المستودع

**المرجع:** `main` عند `b279883a664e5f515a1049fbf7055865d6f5b662`، تاريخ إعداد الحزمة 2026-11-26.

## ملخص التكوين

| البند | ما شوهد في الشجرة |
|---|---|
| لغة المستودع الأساسية | C# |
| Target Framework | `net10.0` للمشاريع الأساسية |
| SDK pin | `10.0.404` في `global.json` مع `rollForward: latestPatch` |
| Product Host projects | `RetailFlow.Api`, `Application`, `Domain`, `Infrastructure` |
| Product test projects | `RetailFlow.Api.IntegrationTests`, `RetailFlow.Domain.UnitTests` |
| Persistence | EF Core SQL Server provider |
| Database في CI | SQL Server 2022 container |
| API style | ASP.NET Core Minimal APIs |
| Frontend source | لم يُعثر على ملفات `.tsx`, `.ts`, `.jsx`, `.js` في المستودع عند هذا الـ commit |
| Deployment manifests | لم يُعثر على Dockerfile/docker-compose أو deployment workflow مستقل |
| OpenAPI | `AddOpenApi` و`MapOpenApi` في Development فقط |

## إحصاء شجرة الملفات

هذه أرقام ناتجة من شجرة Git recursive عند baseline المحدد، وليست قياساً لتغطية مراجعة سطرية:

- 351 tracked blob files.
- 14 ملف مشروع `.csproj`: 6 ضمن المنتج والاختبارات الأساسية، و8 ضمن التجارب.
- 64 ملف C# في `src/` خارج مجلدات migrations.
- 11 ملف C# ضمن migrations/model snapshot في `src/`.
- 6 ملفات C# للاختبارات الأساسية تحت `tests/`.
- 49 ملفاً تحت `experiments/IdentitySpike/`.
- 60 ملفاً تحت `experiments/SessionTransportSpike/`.
- 171 ملف C# إجمالاً في المستودع.

## بنية Product Host

```text
src/
├── RetailFlow.Api/             # HTTP host, route groups, middleware, auth/session adapter
├── RetailFlow.Application/    # Contracts and service interfaces
├── RetailFlow.Domain/         # Entities, enums, domain behavior and invariants
└── RetailFlow.Infrastructure/ # EF Core, SQL Server, implementations and DI

tests/
├── RetailFlow.Api.IntegrationTests/
└── RetailFlow.Domain.UnitTests/

experiments/
├── IdentitySpike/             # Separate ASP.NET Core Identity experiment
└── SessionTransportSpike/     # Separate session/CSRF/Data Protection experiment
```

## ملفات التشغيل والبناء المؤكدة

- `RetailFlow.slnx`: الحل الأساسي، يضم أربعة مشاريع إنتاجية ومشروعَي اختبار.
- `global.json`: يحدد SDK.
- `.github/workflows/ci.yml`: Workflow واحد شوهد في الشجرة، يبني المنتج والتجارب ويشغّل الاختبارات وSQL migrations وPlaywright.
- `src/RetailFlow.Api/appsettings.json`: إعدادات Logging فقط؛ لا يحتوي `AllowedHosts` أو connection string.
- `src/RetailFlow.Api/Properties/launchSettings.json`: Development profile على `https://localhost:56062` و`http://localhost:56063`.

## حدود الفحص

تم فحص شجرة المستودع، ملفات المشاريع الأساسية، نقاط الدخول والـ endpoint groups، الـ middleware، عقود Application، Domain entities الرئيسية، DI وDbContext، الخدمات الأساسية للكتالوج والهوية والسلة والـ checkout والطلبات، إعدادات EF الأساسية، migrations المحورية، اختبارات المنتج الظاهرة، وCI workflow.

**لم يتم** تنفيذ clone/build/test محلياً داخل بيئة العمل هذه، ولم يتم تنفيذ مراجعة سطرية لكل 64 ملف C# أساسي أو لكل ملفات التجارب، ولم يتم فحص نظام Production حي أو تنفيذ اختبار اختراق. لذلك لا يجوز قراءة هذا baseline على أنه Full Code Audit أو Security Certification.
