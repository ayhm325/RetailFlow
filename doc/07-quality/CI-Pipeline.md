# GitHub Actions CI Pipeline

**Workflow:** `.github/workflows/ci.yml` — `RetailFlow CI`

## Triggering

- Push إلى `main` أو `feature/**`.
- Pull Request إلى `main`.
- `workflow_dispatch`.

## Job

`build-and-test` على `ubuntu-24.04`.

## مراحل التنفيذ

1. Checkout.
2. تشغيل SQL Server 2022 container مؤقت مع SA password عشوائي masked.
3. إعداد SDK من `global.json`.
4. Cache NuGet.
5. Restore .NET local tools من `experiments/IdentitySpike/dotnet-tools.json`.
6. Build `RetailFlow.slnx` Release.
7. Build `IdentitySpike` solution Release.
8. Build `SessionTransportSpike` solution Release.
9. Build SessionTransportSpike Playwright solution Release.
10. تطبيق migrations لكل من IdentitySpike وSessionTransportSpike وProduct Host على SQL databases المحددة في workflow.
11. تثبيت Playwright Chromium وOS dependencies.
12. تشغيل Product Host Unit & Integration Tests.
13. تشغيل IdentitySpike Unit & SQL Integration Tests.
14. تشغيل SessionTransportSpike Unit & SQL Integration Tests.
15. تشغيل SessionTransportSpike Playwright Browser Tests.
16. Upload TRX وPlaywright diagnostics عند `always()`.
17. حذف SQL container عند `always()`.

## ما لا يفعله workflow الظاهر

- لا يوجد deploy إلى staging/production.
- لا توجد خطوة صريحة للـ SAST أو dependency vulnerability gate أو DAST.
- لا يوجد artifact واضح لملف OpenAPI منشور.
- لا يوجد smoke test لبيئة Production فعلية.

## تحسينات مقترحة

- إضافة `dotnet format --verify-no-changes` أو formatter policy.
- إضافة dependency and secret scanning.
- فصل build/test للـ Product عن experiments إذا كان لها release lifecycle مختلف.
- حفظ test results مع summary واضح لكل suite.
- إضافة migration script artifact ومراجعة destructive changes.
- بناء container image only after Dockerfile and deployment model are defined.
- إضافة branch protection required checks مع توثيق check names الفعلية.

لا تستخدم هذا الملف للقول إن آخر CI نجح؛ هو وصف Workflow source.
