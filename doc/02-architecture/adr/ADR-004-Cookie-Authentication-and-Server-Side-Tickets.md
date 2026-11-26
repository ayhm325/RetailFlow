# ADR-004: Cookie authentication مع server-side tickets

**Status:** Updated by [ADR-006](file:///c:/Users/ayhm3/source/RetailFlow/doc/02-architecture/adr/ADR-006-ASP.NET-Core-Identity-Adoption.md) (ASP.NET Core Identity Adoption)

## Context

Product Host يستخدم ASP.NET Core Identity (`IdentityConstants.ApplicationScheme`) مع Cookie Authentication، ويخزن ticket في SQL Server عبر `ITicketStore` مخصص (`SqlSessionTicketStore`) مع `SecurityStampValidator` وفحص `IsActive`.

## Observed decision

- cookie name: `__Host-RetailFlow.Session`.
- HttpOnly=true, SecurePolicy=Always, SameSite=Lax, Path=/.
- SlidingExpiration=true وExpireTimeSpan=12 hours.
- `SqlSessionTicketStore` يخزن serialized ticket مع user/store IDs وتاريخ انتهاء وآخر استخدام.
- logout يستدعي `SignOutAsync` ويزيل المفتاح من SQL store.

## Evidence

`ProductHostExtensions.cs`, `AuthEndpoints.cs`, `SqlSessionTicketStore.cs`, `IdentityConfiguration.cs`.

## Consequences

جلسات الخادم قابلة للإلغاء مركزياً عن طريق حذف ticket، لكن الخدمة الحالية لا تعرض إدارة جلسات المستخدم أو تنظيفاً دورياً للسجلات المنتهية ضمن Product Host.

## Alternatives not evaluated

ASP.NET Core Identity بالكامل في Product Host، token-based browser auth، أو Redis ticket store.

## Open questions

Data Protection key persistence/sharing، session revocation عند تعطيل الحساب، سياسة idle/absolute timeout، تدوير الجلسة بعد login، وإدارة جلسات متعددة الأجهزة.
