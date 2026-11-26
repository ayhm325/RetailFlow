# ADR-002: SQL Server وEF Core للمثابرة

**Status:** Inferred from implementation — Needs Approval

## Context

المشروع يعرّف `RetailFlowDbContext`, `UseSqlServer`, configurations وEF migrations. CI يشغّل SQL Server 2022 container ويطبق migrations الخاصة بالمنتج.

## Observed decision

استخدام SQL Server كقاعدة البيانات الأساسية وEF Core للتخطيط والاستعلام وإدارة migrations.

## Evidence

`src/RetailFlow.Infrastructure/DependencyInjection.cs`, `RetailFlowDbContext.cs`, `ConnectionStringResolver.cs`, `src/RetailFlow.Infrastructure/Persistence/Migrations/`, `.github/workflows/ci.yml`.

## Consequences

- بعض الضوابط تعتمد على SQL Server-specific features مثل `rowversion` و`ExecuteDeleteAsync` وerror numbers 2601/2627.
- اختبارات التكامل تحتاج SQL Server فعلياً؛ InMemory provider ليس بديلاً مكافئاً لهذه القيود.
- الترحيلات جزء من release procedure ويجب مراجعة SQL الناتج قبل الإنتاج.

## Alternatives not evaluated

PostgreSQL أو provider abstraction متعدد قواعد البيانات.

## Open questions

إصدار SQL Server الإنتاجي، HA/backup strategy، encryption at rest، recovery objectives، وmigration rollback strategy.
