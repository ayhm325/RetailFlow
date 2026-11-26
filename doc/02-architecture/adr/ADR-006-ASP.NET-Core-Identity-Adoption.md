# ADR-006: اعتماد ASP.NET Core Identity الرسمي في Product Host

**Status:** Owner Decision Implemented — Pending Final Sign-off (2026-11-26)

## Context
كان Product Host يعتمد سابقاً على نموذج مستخدمين ومصادقة مخصص (`AppUser`, `AuthService`, `PasswordHasherService`) بدون دعم مدمج لقفل الحسابات (`Lockout`) أو `SecurityStamp` أو بنية استعادة كلمة المرور والمصادقة الثنائية (`GAP-009`)، بينما كان مشروع التجربة المنفصل `experiments/IdentitySpike/` قد أثبت إمكانية تكييف ASP.NET Core Identity مع تعدد المتاجر. قرر مالك المشروع اعتماد **ASP.NET Core Identity** رسمياً كمصدر الحقيقة الوحيد للهوية في Product Host مع الحفاظ على عزل المتاجر ومفاتيح `Guid` وجلسات SQL Server.

## Decision

1. **Identity Model & Guid Keys:**
   - إنشاء `RetailFlowIdentityUser : IdentityUser<Guid>` و`RetailFlowIdentityRole : IdentityRole<Guid>` داخل `RetailFlow.Infrastructure.Identity` دون إدخال اعتماد مباشر على `Microsoft.AspNetCore.Identity` في `RetailFlow.Domain` أو `RetailFlow.Application`.
   - الحفاظ على `Guid` كمفتاح أساسي (`Id`) لمنع كسر العلاقات مع `Orders.CustomerId` و`CheckoutRequests.CustomerId` و`SessionTickets.UserId`.
   - تعيين `UserName` كمعرّف داخلي فريد مبني على `Guid` (`id.ToString("N")`) بينما يبقى `Email` هو معرّف تسجيل الدخول، لتفادي تصادم `UserNameIndex` العالمي عند تكرار البريد بين المتاجر.

2. **DbContext & Database Schema (`AspNetUsers` + `AspNetRoles`):**
   - ترقية `RetailFlowDbContext` إلى `IdentityDbContext<RetailFlowIdentityUser, RetailFlowIdentityRole, Guid>` مع الحفاظ على `IDataProtectionKeyContext` وجميع جداول التجارة و`SessionTickets` و`DataProtectionKeys`.
   - إعادة تسمية `AppUsers` إلى `AspNetUsers` في Migration `20261126094533_AdoptAspNetCoreIdentity` دون حذف المستخدمين، وتحويل `AppUsers.Role` (`1/2/3`) إلى `AspNetRoles` و`AspNetUserRoles` (`Customer`, `MerchantOwner`, `PlatformAdmin`).
   - الحفاظ على فهرسَي التفرد المقيّدين بالمتجر: `UX_AspNetUsers_StoreId_NormalizedEmail` (`WHERE [StoreId] IS NOT NULL`) و`UX_AspNetUsers_Platform_NormalizedEmail` (`WHERE [StoreId] IS NULL`).

3. **Multi-Tenancy & Fail-Closed `ScopedUserStore`:**
   - تطبيق `IdentityLookupScope` (`Store`, `Platform`, `SystemProvisioning`) و`RetailFlowIdentityScopeMiddleware` الذي يعمل **بعد `UseRouting()` وقبل `UseAuthentication()`**، لضمان توفر نطاق المتجر قبل استدعاء Cookie Authentication أو `SecurityStampValidator`.
   - تطبيق `ScopedUserStore` (`UserStore<RetailFlowIdentityUser, RetailFlowIdentityRole, RetailFlowDbContext, Guid>`) بنمط Fail-Closed على جميع الاستعلامات والبحث والتعديل وتعيين الأدوار، بالإضافة إلى `RetailFlowUserValidator` لفحص التفرد ضمن النطاق.

4. **Cookie Scheme, `SqlSessionTicketStore`, & Security Stamp:**
   - توحيد Cookie Scheme على `IdentityConstants.ApplicationScheme` مع الحفاظ على `__Host-RetailFlow.Session` (`HttpOnly`, `SecurePolicy=Always`, `SameSite=Lax`, `SlidingExpiration`, `12 hours`) و`SqlSessionTicketStore` (`ITicketStore`).
   - دمج `SecurityStampValidator.ValidatePrincipalAsync` مع فحص `IsActive` و`store_id` في `RetailFlowCookieEvents.ValidatePrincipal` لضمان الإبطال الفوري للجلسات عند تعطيل الحساب أو تغيير/إعادة تعيين كلمة المرور.

5. **Password Hash Compatibility & OWASP Policy:**
   - اعتماد `RetailFlowPasswordHasher : IPasswordHasher<RetailFlowIdentityUser>` باستخدام Identity V3 (`PBKDF2-HMAC-SHA512`, `220,000` تكرار وفق OWASP Cheat Sheet)، مع دعم التحقق من الصيغة القديمة `v1.{Iterations}.{Salt}.{Hash}` (`310,000..1,000,000`) وإرجاع `SuccessRehashNeeded` لترقيتها تلقائياً عند أول تسجيل دخول ناجح.

6. **Lockout, Password Reset, Email Confirmation, & MFA:**
   - تفعيل `Lockout` (`MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 minutes`, `lockoutOnFailure: true`).
   - تفعيل `DataProtectorTokenProvider` لتوليد والتحقق من رموز إعادة تعيين كلمة المرور (`forgot-password` / `reset-password` باستجابة عامة تمنع Account Enumeration) وبنية TOTP 2FA وRecovery Codes (`2fa/setup`, `2fa/enable`, `2fa/disable`, `2fa/recovery-codes`).
   - إبقاء `RequireConfirmedEmail = false` حتى تتوفر خدمة إرسال بريد إنتاجية (`IEmailSender`).

## Alternatives Considered & Rejected
- **الإبقاء على `AuthService` و`PasswordHasherService` المخصصين:** مرفوض لأنه يتطلب إعادة بناء Lockout وSecurityStamp وToken Providers و2FA يدوياً.
- **استخدام `IdentityUser` الافتراضي بمفاتيح `string` وتفرد بريد عالمي:** مرفوض لأنه يكسر `Guid` foreign keys وتعدد المتاجر (`StoreId + NormalizedEmail`).
- **نسخ `IdentitySpike` حرفياً:** مرفوض لأن `IdentitySpike` استخدم `string` keys و`UserOnlyStore` بدون Identity Roles الكاملة أو ربط `SqlSessionTicketStore`.

## Consequences & Rollback
- **المزايا:** توحيد نظام الهوية على ASP.NET Core Identity 10 مع الحفاظ الكامل على العزل متعدد المستأجرين و`Guid` keys وجلسات SQL Server.
- **أثر الترقية (Cutover):** الانتقال إلى `IdentityConstants.ApplicationScheme` وClaims Identity قد يتطلب إعادة تسجيل دخول واحدة للجلسات القديمة إن وُجدت قبل الترقية.
- **خطة التراجع (Rollback):** الترحيل `20261126094533_AdoptAspNetCoreIdentity` يحتوي دالة `Down()` تعيد تحويل `AspNetUserRoles` إلى عمود `Role` وإعادة تسمية `AspNetUsers` إلى `AppUsers`؛ مع ضرورة أخذ نسخة احتياطية قبل التطبيق في أي بيئة تشغيلية.
