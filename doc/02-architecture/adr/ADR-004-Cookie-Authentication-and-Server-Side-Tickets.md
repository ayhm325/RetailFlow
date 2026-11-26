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

## Consequences & Current Implementation Reconciliation (2026-11-26)

- جلسات الخادم قابلة للإلغاء مركزياً عبر `SqlSessionTicketStore` (`SessionTickets`).
- **تحديث التنفيذ الفعلي (مقارنة بالوصف الأولي أعلاه):**
  - يفرض `SqlSessionTicketStore` مهلة خمول (`IdleLifetime`) ومهلة مطلقة غير قابلة للتمديد (`AbsoluteExpiresAtUtc`) حسب الدور: `PlatformAdmin` (`1h`/`8h`)، `MerchantOwner` (`4h`/`24h`)، `Customer` (`12h`/`7d`)؛ مع إبقاء `CookieAuthenticationOptions.ExpireTimeSpan = 12 hours` كإطار عام.
  - يقوم `MaintenanceBackgroundService` بحذف تذاكر الجلسات المنتهية دورياً كل دقيقة، ويُبطل `AuthService` + `SqlSessionTicketStore` الجلسات عند تغيير كلمة المرور أو تعطيل الحساب أو تغيير حالة MFA.
  - لا تزال واجهة عرض/إلغاء الأجهزة النشطة للمستخدم غير موجودة.
  - **ملاحظة برمجية مؤجلة:** تعليق `SqlSessionTicketStore.cs` يشير إلى `AUTH-ADR-01` غير موجود في سجل ADRs (المرجع الصحيح هو `ADR-004` / `ADR-006`).

## Alternatives not evaluated

ASP.NET Core Identity بالكامل في Product Host، token-based browser auth، أو Redis ticket store.

## Open questions

Data Protection key persistence/sharing، session revocation عند تعطيل الحساب، سياسة idle/absolute timeout، تدوير الجلسة بعد login، وإدارة جلسات متعددة الأجهزة.
