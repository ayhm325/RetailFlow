# ADR-001: Layered Product Host

**Status:** Inferred from implementation — Needs Architecture Owner Approval  
**Date:** 2026-11-26 (documentation observation date)

## Context

الحل الأساسي يقسم الكود إلى أربعة مشاريع: API وApplication وDomain وInfrastructure، ويجمعها `RetailFlow.slnx` في Product Host واحد.

## Observed decision

- API هو composition/transport layer.
- Application يعرّف العقود والـ service interfaces.
- Domain يملك entities والسلوك.
- Infrastructure يوفّر EF Core implementations وDI.

## Evidence

`RetailFlow.slnx` وملفات `*.csproj`, `src/RetailFlow.Api/Program.cs`, `src/RetailFlow.Infrastructure/DependencyInjection.cs`.

## Consequences observed

- يمكن اختبار Domain منفصلاً.
- الـ Infrastructure هو حد الوصول إلى SQL Server.
- HTTP endpoints لا تتصل مباشرة بـ DbContext في المسارات الأساسية التي تم فحصها، بل تستدعي interfaces/services.
- لا توجد حدود deployment مستقلة بين المجالات في هذا الحل.

## Alternatives not evaluated in source

Vertical slices داخل مشروع واحد، Modular Monolith بحدود modules أقوى، Microservices.

## Open questions

هل هذه هي البنية المستهدفة طويلة المدى؟ ما قيود اعتماد API على Infrastructure مباشرة؟ وهل يجب نقل Composition Root إلى مشروع مستقل؟
