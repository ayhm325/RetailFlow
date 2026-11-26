# استراتيجية الاختبار

## 1. طبقات الاختبار الموجودة

### Domain Unit Tests

المشروع `tests/RetailFlow.Domain.UnitTests` يستخدم xUnit ويغطي Domain invariants وبعض Order transitions. ملفات واضحة: `DomainInvariantsTests.cs`, `OrderDomainTests.cs`.

### API Integration Tests

المشروع `tests/RetailFlow.Api.IntegrationTests` يستخدم `Microsoft.AspNetCore.Mvc.Testing` وSQL Server provider وxUnit، ويضم 18 ملف اختبار ومساعدات:

- `ProductHostIntegrationTests.cs` و`PublicStoreCatalogIntegrationTests.cs`
- `OwaspApiSecurityIntegrationTests.cs` و`EndToEndCommerceAuthIntegrationTests.cs`
- `CartConcurrencyTests.cs` و`CheckoutErrorClassificationTests.cs` و`CheckoutLeaseAndCookieScopeTests.cs`
- `MaintenanceBatchConcurrencyTests.cs` و`AuditPhasesIntegrationTests.cs`
- `IdentityAdoptionIntegrationTests.cs` و`IdentitySecurityHardeningTests.cs`
- `PlatformAdminMfaHardeningTests.cs` و`SessionLifetimeHardeningTests.cs`
- `PasswordHasherTests.cs` و`PasswordPolicyHardeningTests.cs`
- `DataProtectionHardeningTests.cs` و`EmailHardeningTests.cs` و`TestDataProtectionCertHelper.cs`

الاختبارات تشمل startup/health/OpenAPI، security headers، host filtering، forwarded headers، exception handling، SPA fallback semantics، catalog, pagination, tenancy, delivery zones, store lifecycle، auth/CSRF، BOLA وrate limiting وcommerce flow وidempotency وreservation timeout في الحالات المكتوبة.

### Experiments tests

توجد Unit/SQL Integration/Playwright suites تحت `experiments/IdentitySpike` و`experiments/SessionTransportSpike`. هذه تثبت سلوك التجارب فقط، ولا تنقل إثباتها تلقائياً إلى Product Host.

## 2. بيئة الاختبار في CI

- GitHub Actions على `ubuntu-24.04`.
- SQL Server 2022 container مؤقت على port 1433.
- إعداد `RETAILFLOW_CONNECTION` و`ConnectionStrings__RetailFlow` للاختبارات.
- SDK يتم اختياره من `global.json`.
- Build للحل الأساسي والتجارب.
- تطبيق EF migrations على DB مؤقتة.
- تثبيت Chromium وتشغيل Playwright tests الخاصة بـ SessionTransportSpike.
- رفع TRX وPlaywright diagnostics كـ artifacts.

التفاصيل في `07-quality/CI-Pipeline.md`.

## 3. أوامر تشغيل Product Host tests

```bash
dotnet restore RetailFlow.slnx
dotnet build RetailFlow.slnx --configuration Release
dotnet test RetailFlow.slnx --configuration Release --logger "console;verbosity=normal"
```

الاختبارات التكاملية تحتاج SQL Server واتصال `RETAILFLOW_CONNECTION` مضبوطاً. تحقق من fixture setup قبل تشغيلها خارج CI.

## 4. فجوات التحقق المتبقية

- **Performance & Load:** query plans/index usage، pagination limits، N+1 profiling، load tests (`GAP-018`).
- **API:** contract tests شاملة لكل malformed/oversized body وunexpected content types.
- **Browser E2E للمنتج:** لا يوجد React SPA أو Playwright tests للـ Product Host بعد (موجود فقط لـ `SessionTransportSpike`).
- **Operational Resilience:** SQL outage recovery drills، backup/restore drills (`GAP-011`).

## 5. تفسير نتائج الاختبار

وجود test method لا يثبت أنها نجحت في أحدث run. وجود CI workflow لا يثبت نجاح تشغيل محدد. سجل run ID وcommit SHA والـ artifacts عند اعتماد تقرير جودة.
