# ADR-004: Cookie authentication مع server-side tickets

**Status:** Inferred from implementation — Needs Security Approval

## Context

Product Host يستخدم ASP.NET Core Cookie Authentication، ويخزن ticket في SQL Server عبر `ITicketStore` مخصص.

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
