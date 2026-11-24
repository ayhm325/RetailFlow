# توثيق حالة التنفيذ — Phase 06: إثبات المفهوم لتعدد هوية العملاء (Identity Spike)

- **المسار التجريبي:** `experiments/IdentitySpike/`
- **المرجع المعماري:** دراسة إثبات المفهوم لتعدد هوية العملاء (`Phase 06 — v1.0 — 22 نوفمبر 2026`)
- **تاريخ آخر تحديث للتقرير:** `2026-11-24`
- **نقطة التوقف الحالية:** اكتمل تنفيذ واختبار `IdentityScopeResolver` و`IdentityScopeMiddleware` وربطهما بـ Route Group الفعلي (`/api/stores/{slug}/customers`) في `Program.cs` مع إثبات عزل الطلبات واستقلال مثيل `IdentityLookupScopeAccessor` عبر `WebApplicationFactory` وSQL Server.
- **الخطوة التالية بعد تثبيت هذه النقطة:** استكمال `Authentication` و`Authorization` داخل `Program.cs` (بعد حسم `AUTH-ADR-01`) وبناء Minimal APIs (`register`, `login`, `confirm-email`, `forgot/reset-password`, `me`).

### ملخص حالة المراحل

| المرحلة | الحالة |
| :--- | :--- |
| **0 — Baseline** | الأساس المحلي منجز، وCI متبقٍ |
| **1 — Model** | نموذج Identity الأساسي منجز، وقرارات Profile/Membership متبقية |
| **2 — Scoped Store** | منجزة، بما في ذلك Resolve/Scope Middleware وربط Route Group الفعلي لعملاء المتاجر |
| **3 — Managers & APIs** | جزئية: Managers وRoute Group النطاق منفذة؛ Minimal Auth APIs وSignInManager في التطبيق متبقية |
| **4 — Lifecycle** | إثبات الخدمات منجز؛ HTTP endpoints والسياسات متبقية |
| **5 — Security** | لم تبدأ كتنفيذ إنتاجي؛ اختبار ترتيب Authentication مع Scope منفذ في TestServer فقط |
| **6 — SQL/CI** | جزئية: 33 اختبار تكامل ناجحاً؛ CI والتزامن وShared Key Ring متبقية |

---

## 1. ملخص ما تم إنجازه من خطة Phase 06

### أ. المرحلة 0 — Baseline (إعداد الحل وتثبيت الإصدارات)
- تثبيت إصدار .NET SDK في [`global.json`](../global.json) (`10.0.404`, `rollForward: latestPatch`).
- تثبيت أداة `dotnet-ef` محلياً في [`experiments/IdentitySpike/dotnet-tools.json`](../experiments/IdentitySpike/dotnet-tools.json) بالإصدار `10.0.15`.
- إنشاء ملف الحل [`RetailFlow.IdentitySpike.slnx`](../experiments/IdentitySpike/RetailFlow.IdentitySpike.slnx) ويضم:
  - مشروع الـ API: `RetailFlow.IdentitySpike.Api` (`net10.0`)
  - مشروع اختبارات الوحدة: `RetailFlow.IdentitySpike.IdentityScopeUnitTests`
  - مشروع اختبارات التكامل: `RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests`
- تثبيت حزم `Microsoft.AspNetCore.Identity.EntityFrameworkCore` و `Microsoft.EntityFrameworkCore.SqlServer` و `Microsoft.EntityFrameworkCore.Design` و `Microsoft.AspNetCore.OpenApi` و `Microsoft.AspNetCore.Mvc.Testing` و `Microsoft.AspNetCore.TestHost` عند الإصدار `10.0.15`، مع `xUnit 2.9.3`.
- ضبط [`.gitignore`](../.gitignore#L66-L72) لحفظ `experiments/IdentitySpike/artifacts/` وتجاهل ملفات الإعدادات المحلية (`appsettings.Development.json`, `appsettings.Local.json`, `secrets.json`).

---

### ب. المرحلة 1 — Model (نموذج الهوية وقاعدة البيانات)
- **كيانات النطاق (Domain):**
  - [`AccountKind.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Domain/Identity/AccountKind.cs): يميز بين `BackOffice = 1` و `StoreCustomer = 2`.
  - [`ApplicationUser.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Domain/Identity/ApplicationUser.cs): يرث من `IdentityUser` ويضيف `AccountKind` و `Guid? StoreId`.
  - [`SpikeStore.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Domain/Stores/SpikeStore.cs): كيان المتجر التجريبي (`Id`, `Name`, `Slug`, `IsActive`).
- **إعدادات EF Core و SQL Server في [`IdentitySpikeDbContext.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Persistence/IdentitySpikeDbContext.cs):**
  - يرث من `IdentityUserContext<ApplicationUser>` (بدون جدول Roles، توافقاً مع `ID-ADR-05`).
  - قيد تحقق `CK_AspNetUsers_AccountKind_StoreId`: يفرض `(AccountKind = 1 AND StoreId IS NULL) OR (AccountKind = 2 AND StoreId IS NOT NULL)`.
  - فهرس فريد للعملاء `UX_AspNetUsers_StoreId_NormalizedEmail`: على `(StoreId, NormalizedEmail)` للسماح بنفس البريد في متاجر مختلفة ومنعه داخل نفس المتجر.
  - فهرس فريد مُصفّى `UX_AspNetUsers_BackOffice_NormalizedEmail`: على `NormalizedEmail` بشرط `[StoreId] IS NULL AND [NormalizedEmail] IS NOT NULL`.
  - فهرس فريد على `Stores(Slug)` وعلاقة مفتاح أجنبي `StoreId -> Stores(Id)` بسلوك حذف `Restrict` (`ON DELETE NO ACTION`).
  - جعل `Email` و `NormalizedEmail` مطلوبين بحد أقصى `256`.
- **الترحيل والتحقق من SQL:**
  - توليد Migration: `20261123104928_InitialIdentityModel` وسكربت [`InitialIdentityModel.sql`](../experiments/IdentitySpike/artifacts/InitialIdentityModel.sql).
  - إعداد سكربت التحقق من القيود والفهارس [`VerifyIdentityConstraints.sql`](../experiments/IdentitySpike/artifacts/VerifyIdentityConstraints.sql) (يختبر 5 حالات SQL داخل Transaction يتم التراجع عنها).

---

### ج. المرحلة 2 — Scoped Store (عزل البحث في UserStore وتسجيل DI)
- **نموذج نطاق البحث:**
  - [`IdentityLookupScope.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IdentityLookupScope.cs): كائن غير قابل للتعديل مع مصانع ثابتة `ForBackOffice()` و `ForStoreCustomer(Guid storeId)` (يرفض `Guid.Empty`).
  - [`IIdentityLookupScopeAccessor.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IIdentityLookupScopeAccessor.cs) و [`IdentityLookupScopeAccessor.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IdentityLookupScopeAccessor.cs): يحفظ نطاق الطلب الحالي ويمنع إعادة تعيينه أكثر من مرة عبر `Interlocked.CompareExchange`.
- **مخزن المستخدمين المعزول [`ScopedUserStore.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/ScopedUserStore.cs):**
  - يرث من `UserOnlyStore<ApplicationUser, IdentitySpikeDbContext>` ويعمل بمبدأ **Fail-Closed** (يرمي `InvalidOperationException` إذا لم يُحدد النطاق).
  - تقييد الاستعلامات والبحث: `Users`, `FindByIdAsync`, `FindByEmailAsync`, `FindByNameAsync`, `FindUserAsync`, و `FindByLoginAsync` (مع Fail-Closed عند غياب النطاق).
  - حراسة `CreateAsync` و `UpdateAsync` و `DeleteAsync` (ترجع `IdentityResult.Failed` بكود `IdentityScopeViolation` عند مخالفة النطاق أو عدم وجود المستخدم في النطاق الحالي).
  - حماية العمليات الحساسة عبر `EnsurePersistedUserIsInCurrentScopeAsync` (التحقق من خصائص الكائن في الذاكرة **ومن وجوده فعلياً في SQL Server ضمن النطاق الحالي** لمنع تمرير كائن مزور):
    - **Claims:** `GetClaimsAsync`, `AddClaimsAsync`, `ReplaceClaimAsync`, `RemoveClaimsAsync`.
    - **Logins:** `AddLoginAsync`, `RemoveLoginAsync`, `GetLoginsAsync`.
    - **Tokens:** `FindTokenAsync`, `AddUserTokenAsync`, `RemoveUserTokenAsync`.
  - حماية عمليات **Lockout** و **Security Stamp** عبر **Hybrid Scope Validation** (`EnsureUserMatchesScopeOrIsNewAsync`): تتحقق من نطاق الكائن في الذاكرة، وتستعلم عن `user.Id` في `AspNetUsers` دون فلتر النطاق — فتسمح بالمستخدم الجديد غير المحفوظ بعد (لدعم `UserManager.CreateAsync`)، وترفض أي كائن يحمل `Id` محفوظاً في متجر/نطاق آخر:
    - **Lockout:** `GetLockoutEndDateAsync`, `SetLockoutEndDateAsync`, `GetAccessFailedCountAsync`, `IncrementAccessFailedCountAsync`, `ResetAccessFailedCountAsync`, `GetLockoutEnabledAsync`, `SetLockoutEnabledAsync`.
    - **Security Stamp:** `GetSecurityStampAsync`, `SetSecurityStampAsync`.
- **واجهة قدرات MVP [`ScopedUserStoreFacade.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/ScopedUserStoreFacade.cs):**
  - تغلف `ScopedUserStore` وتكشف فقط واجهات Identity المطلوبة لـ MVP (`Password`, `Email`, `SecurityStamp`, `Lockout`, `Claims`, `QueryableUsers`) مع استبعاد الواجهات خارج النطاق (`AuthenticationTokens`, `ExternalLogins`, `PhoneNumber`, `TwoFactor`, `Passkeys`, `Roles`).
- **حل نطاق المتجر عبر HTTP:**
  - [`IIdentityScopeResolver.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IIdentityScopeResolver.cs) و [`IdentityScopeResolver.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IdentityScopeResolver.cs): يحلان `Store Slug` إلى `StoreId` للمتاجر النشطة فقط، باستخدام `AsNoTracking` وتمرير `CancellationToken`. يتم تطبيع القيمة الواردة باستخدام `ToLowerInvariant()` قبل الاستعلام، مع إبقاء عمود `Slug` دون تطبيق تحويل عليه داخل SQL query.
  - [`IdentityScopeMetadata.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IdentityScopeMetadata.cs): Metadata تعتمد `AccountKind` الحالي لتحديد `StoreCustomer` أو `BackOffice`.
  - [`IdentityScopeMiddleware.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Infrastructure/Identity/IdentityScopeMiddleware.cs): يقرأ Endpoint Metadata وRoute Value باسم `slug` بعد Routing؛ ينشئ `ForStoreCustomer(storeId)` أو `ForBackOffice()`، ولا يستدعي Resolver عند غياب Metadata أو في BackOffice. يعيد `404` عند غياب slug أو عدم حل متجر نشط، ولا يسمح بوصول الطلب إلى Endpoint.
  - يظل `IdentityLookupScopeAccessor` Scoped وغير قابل لإعادة التعيين خلال الطلب الواحد؛ واختبارات HTTP أثبتت عدم تسرب Scope بين طلبين متتاليين.
- **التسجيل في [`Program.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Program.cs):**
  - تسجيل `IIdentityLookupScopeAccessor` و `IIdentityScopeResolver` كـ `Scoped`، وإضافة `AddDataProtection()`.
  - تسجيل `ScopedUserStore` و `AddIdentityCore<ApplicationUser>` مع `options.User.RequireUniqueEmail = false` و `.AddUserStore<ScopedUserStoreFacade>()` و `.AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(TokenOptions.DefaultProvider)`.
  - إضافة نقطة فحص اتصال قاعدة البيانات `GET /diagnostics/database` في بيئة التطوير.
  - إضافة `UseRouting()` صراحةً ثم `UseMiddleware<IdentityScopeMiddleware>()` بعد `UseHttpsRedirection()`، وربط Route Group `/api/stores/{slug}/customers` بـ `IdentityScopeMetadata(AccountKind.StoreCustomer)` مع نقطة `/scope` للتحقق؛ لم تتم إضافة `UseAuthentication()` أو `UseAuthorization()` إلى التطبيق الفعلي بعد.

---

### د. الاختبارات المكتوبة حتى الآن
1. **اختبارات الوحدة (`IdentityScopeUnitTests` — 14 اختباراً):**
   - `IdentityLookupScopeTests.cs` (3 اختبارات).
   - [`IdentityLookupScopeAccessorTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentityScopeUnitTests/Infrastructure/Identity/IdentityLookupScopeAccessorTests.cs) (5 اختبارات شاملة رفض التعيين المكرر `SetCurrent` والتحقق من نص الخطأ والحفاظ على مرجع النطاق الأصلي).
   - `UserStoreCapabilitiesTests.cs` (اختبار واحد يتحقق من حصر قدرات `UserManager` في قدرات MVP فقط).
   - [`IdentityScopeResolverContractTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentityScopeUnitTests/Infrastructure/Identity/IdentityScopeResolverContractTests.cs) (5 حالات: رفض slug الفارغ/المسافات، واحترام `CancellationToken`).
2. **اختبارات التكامل مع SQL Server وHTTP TestServer (`IdentitySqlServerIntegrationTests` — 33 اختباراً):**
   - [`TestAssemblyConfiguration.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/TestAssemblyConfiguration.cs): تعطيل التوازي (`DisableTestParallelization = true`) لتجنب Deadlocks عند مشاركة قاعدة بيانات واحدة بين مجموعات الاختبار.
   - `ScopedUserStoreLookupTests.cs` (اختباران): عزل `Users` و `FindById/Email/Name`، وعزل `BackOffice`، واختبار Fail-Closed.
   - `ScopedUserStoreClaimsTests.cs` (اختباران): منع قراءة/إضافة/استبدال/حذف Claims لمتجر آخر أو لكائن مزور، واختبار `UserManagerGetUsersForClaimAsync_ReturnsOnlyUsersFromCurrentStore` الذي يثبت أن `UserManager.GetUsersForClaimAsync` لا يعيد مستخدمين من متجر آخر رغم تطابق الـ Claim.
   - `ScopedUserStoreLoginTests.cs` (اختبار واحد): عزل عمليات `IUserLoginStore` ومنع الكائنات المزورة واختبار Fail-Closed.
   - `ScopedUserStoreSecurityStateTests.cs` (اختبار واحد): عزل عمليات `IUserLockoutStore` و `IUserSecurityStampStore` ورفض الكائنات المزورة.
   - `ScopedUserStoreTokenTests.cs` (اختبار واحد): عزل عمليات `IUserAuthenticationTokenStore` ورفض الكائنات المزورة.
   - `UserManagerStoreScopingTests.cs` (اختبار واحد): السماح بنفس البريد الإلكتروني في متجرين مختلفين ورفض التكرار داخل نفس المتجر.
   - [`UserManagerLifecycleTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/UserManagerLifecycleTests.cs) (4 اختبارات): تأكيد البريد، إعادة تعيين كلمة المرور وإبطال التوكن السابق، Lockout وانتهاء مدته، والتحقق من `CheckPasswordSignInAsync`.
   - [`IdentityScopeResolverTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeResolverTests.cs) (اختبار SQL واحد): يحل المتجر النشط فقط، ويرفض غير النشط وغير الموجود، ويتحقق من slug بأحرف كبيرة.
   - [`IdentityScopeMiddlewareTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeMiddlewareTests.cs) (11 اختباراً عبر `TestServer` و `DefaultHttpContext`): StoreCustomer، BackOffice، Endpoint دون Metadata (عبر `TestServer` وعبر `DefaultHttpContext` + `SpyIdentityScopeResolver`)، Store غير قابل للحل وإرجاع 404، عزل Scope بين طلبين متتاليين، و5 حالات `MissingOrInvalidSlug` (غياب المفتاح، `null`، `""`، `" "`، ونوع غير `string` مثل `123` مع إثبات عدم استدعاء Resolver وعدم ضبط Scope).
   - [`IdentityScopeAuthenticationOrderingTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeAuthenticationOrderingTests.cs) (اختبار واحد عبر `TestServer`): يثبت أن Fake Authentication Handler يرى Scope بعد أن يضبطه Middleware في Pipeline صريح الترتيب.
   - [`IdentitySpikeApiFactory.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentitySpikeApiFactory.cs) و `IdentitySpikeApiFactoryTests.cs` (اختباران باستخدام `WebApplicationFactory`): تشغيل API الفعلي داخل TestServer والتحقق من اتصال قاعدة البيانات، والتحقق من تسجيل Resolver وAccessor كـ Scoped عبر DI scopes مستقلة.
   - [`IdentityScopeRouteGroupIntegrationTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeRouteGroupIntegrationTests.cs) (5 اختبارات باستخدام `WebApplicationFactory` وSQL Server الفعلي): حل المتجر النشط عبر `/api/stores/{slug}/customers/scope`، إرجاع `404` للمتجر غير الموجود وغير النشط، عزل Scope لمتجرين متزامنين، واستقلال الطلبات المتعاقبة (`A → B → A`).
   - [`IdentityLookupScopeAccessorLifetimeTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityLookupScopeAccessorLifetimeTests.cs) و [`AccessorInstanceTracker.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/AccessorInstanceTracker.cs) (اختبار واحد): إثبات إنشاء مثيل مستقل من `IIdentityLookupScopeAccessor` لكل طلب HTTP (`Assert.NotSame`) واحتفاظ كل مثيل بحالة متجره عبر SQL Server الفعلي.

> **نتائج التشغيل الفعلي بتاريخ `2026-11-24`:**
> - **`IdentityScopeUnitTests`: `14 / 14` Passed، `0 Failed · 0 Skipped`.**
> - **`IdentitySqlServerIntegrationTests`: `33 / 33` Passed، `0 Failed · 0 Skipped`.** وتشمل اختبارات SQL Server الفعلية واختبارات HTTP باستخدام `TestServer` و `WebApplicationFactory`.
> - **Full Solution Regression (`RetailFlow.IdentitySpike.slnx`): `47 / 47` Passed (`0 Failed · 0 Skipped`)** ضمن تشغيل واحد كامل للحل بعد جميع التعديلات.
> - اختبار API Factory نفذ `SELECT 1` على قاعدة SQL Server.
> - تحذير `Failed to determine the https port for redirect` ظهر في `TestServer` لأن الاختبار لا يهيئ منفذ HTTPS؛ لم يمنع اختبار الاتصال أو نجاح الاختبارات، لكنه لا يثبت سلوك HTTPS Redirection.

### هـ. حدود ما تم إثباته حتى الآن
- `Email Confirmation` مثبت على مستوى الخدمة (`UserManager`)، وليس إرسال البريد فعلياً.
- `Password Reset` مثبت على مستوى الخدمة (`UserManager`)، ولم ننفذ `Forgot Password Endpoint` بعد.
- `Lockout` مثبت على مستوى الخدمة (`UserManager` و `SignInManager.CheckPasswordSignInAsync`)، لكن عزل حسابين بين متجرين عند فشل الدخول لم يُختبر بعد.
- `SecurityStamp` تم التحقق من تغييره وحفظه في SQL Server وإبطال Reset Token السابق.
- `Claims`: تم إثبات عزل عمليات Claims المرتبطة بمستخدم محدد، وكذلك مسار البحث `UserManager.GetUsersForClaimAsync` باستخدام SQL Server الفعلي عبر `ScopedUserStoreFacade` و `ScopedUserStore`.
- `Data Protection` مفعّل في الـ DI، لكن `Shared Persistent Key Ring` لتعدد النسخ لم يُنفذ بعد.
- اختبارات `IdentityScopeAuthenticationOrderingTests` تستخدم `TestServer` وFake Authentication Handler؛ وهي تثبت الترتيب الصريح في الـ Pipeline الاختباري، بينما اختبارات `IdentityScopeRouteGroupIntegrationTests` و `IdentityLookupScopeAccessorLifetimeTests` تثبت تكامل Route Group لعملاء المتاجر (`/api/stores/{slug}/customers/scope`) مع `Program.cs` وSQL Server الفعلي.
- `UseAuthentication()` و`UseAuthorization()` لم تتم إضافتهما إلى الـ Pipeline الفعلي في `Program.cs`، ولم تُحسم استراتيجية Cookies مقابل JWT (`AUTH-ADR-01`).
- اختبار `IdentityScopeAuthenticationOrderingTests` لا يثبت سلوك الإضافة التلقائية للـ Middleware بواسطة `WebApplication`؛ بل يثبت الترتيب الصريح الذي سنعتمده عند تفعيل Authentication.

---

## 2. ما تبقى لإنهاء Phase 06

### أ. بقايا المراحل 0 و 1
- **المرحلة 0 (Baseline):** إعداد خط CI يطابق إصدارات `.NET 10` واختبارات SQL Server، وتوثيق Commit SHA النهائي للتغييرات الحالية *(الـ Baseline Commit موجود مسبقاً)*.
- **المرحلة 1 (Model):** إضافة `CustomerProfile` (مع المفتاح البديل `(StoreId, Id)`) و `StoreMembership` إذا تقرر تضمينها في الـ Spike، أو تثبيتها قبل الانتقال لـ Phase 07.
- *(ملاحظة: تم إكمال تغطية `IUserLoginStore` بالكامل وتنظيف ملفات القالب `UnitTest1.cs` و `/weatherforecast`.)*

### ب. المرحلة 3 — Managers & Minimal APIs (الخطوة القادمة مباشرة)
- *(منجز على مستوى `UserManager`/`SignInManager`: إنشاء المستخدمين بنطاق المتجر عبر `UserManager.CreateAsync`، والتحقق من كلمة المرور مع Lockout عبر `SignInManager.CheckPasswordSignInAsync`.)*
- **المتبقي:**
  - إضافة/تهيئة `AddAuthentication()` و `.AddSignInManager()` في `Program.cs` بعد حسم استراتيجية `Cookies` مقابل `JWT` في `AUTH-ADR-01`، وإضافة `AddHttpContextAccessor()` فقط إذا أثبت تصميم الخدمات حاجته إليه. كما يجب وضع `UseAuthentication()` و`UseAuthorization()` صراحة بعد `IdentityScopeMiddleware` واختبار الترتيب داخل التطبيق الفعلي.
  - إضافة Route Group خاص بـ `BackOffice` في `Program.cs` مع `WithMetadata(new IdentityScopeMetadata(AccountKind.BackOffice))` واختباره عبر `WebApplicationFactory` *(تم إنجاز واختبار Route Group الخاص بـ `StoreCustomer` بالفعل مع SQL Server الفعلي)*.
  - تنفيذ الـ Minimal Endpoints واختبارات التكامل عبر HTTP (`WebApplicationFactory`):
    - `POST /api/stores/{slug}/customers/register` (حل `StoreId` من `{slug}`، توليد `UserName` عشوائي فريد عالمياً، معالجة أخطاء التزامن `2601/2627` بأمان).
    - `POST /api/stores/{slug}/customers/login` (جلب المستخدم عبر `FindByEmailAsync` المقيد بالنطاق ثم `CheckPasswordSignInAsync`/`PasswordSignInAsync` برسالة فشل عامة موحدة).
    - التحقق من تسجيل الدخول بنفس البريد في متجرين مستقلين (سيناريوهات **`01`**, **`02`**, **`04`**).

### ج. المرحلة 4 — Lifecycle (دورة حياة الحساب عبر HTTP & السياسات)
- *(منجز على مستوى `UserManager` + SQL Server: `ConfirmEmailAsync`, `ResetPasswordAsync` + `SecurityStamp`, `AccessFailedAsync` + انتهاء Lockout، و `CheckPasswordSignInAsync`.)*
- **المتبقي:**
  - تنفيذ واختبار الـ Endpoints: `POST /api/stores/{slug}/customers/confirm-email` (سيناريو **`11`**: رفض تأكيد حساب متجر A عبر Slug متجر B)، و `forgot-password` / `reset-password` (سيناريوهات **`10`** و **`15`**).
  - اختبار `GetUserAsync` و `Lockout` عبر `SignInManager` بين متجرين مختلفين (سيناريوهات **`08`** و **`09`**: قفل حساب A لا يقفل حساب B).
  - ضبط سياسة كلمات المرور (`PasswordOptions` وفق NIST SP 800-63B-4: حد أدنى 15 حرفاً عند الاعتماد على كلمة المرور وحدها، بدون تعقيد تركيبي إجباري، وقياس أداء `PasswordHasher`) وضبط عمر Token لإعادة التعيين (`DataProtectionTokenProviderOptions`).

### د. المرحلة 5 — Security & Authorization
- تنفيذ واختبار `GET /api/stores/{slug}/customers/me` مع التحقق من مطابقة `Store` Claim للـ `slug` الحالي (سيناريو **`12`**: رفض Token متجر على مسار متجر آخر بـ `403`).
- تنفيذ مسارات `BackOffice` (مثل `/api/backoffice/stores/{storeId}/...`) مع `Authorization Policy` تفحص `StoreMembership` (سيناريو **`13`**).
- إضافة `Rate Limiting` مقسّم (Partitioned) على `IP` وعلى `(StoreId + NormalizedEmail)` لمسارات `Register`, `Login`, `Forgot/Reset`.
- مراجعة وتوحيد جميع رسائل الخطأ العامة لمنع `User Enumeration` (سيناريو **`15`**).

### هـ. المرحلة 6 — SQL Concurrency & Data Protection & CI
- كتابة وتشغيل اختبار التزامن للتسجيل المتوازي بنفس البريد في نفس المتجر (سيناريو **`03`**).
- التحقق من استقرار ومشاركة `Data Protection Key Ring` بين نسختي API عند التحقق من Token تأكيد البريد (سيناريو **`16`**).
- تشغيل كامل مصفوفة الاختبارات (`01`–`16`) على SQL Server حقيقي، وتوثيق تقرير النتائج النهائي (مع أرقام الاختبارات وSQL schema وCommit SHA).

### و. نقطة الاستئناف بعد تثبيت هذا التحديث
- الحالة الحالية: `IdentityScopeResolver` و `IdentityScopeMiddleware` منفذان ومختبران، ومسجلان في DI وPipeline داخل `Program.cs` مع ربط Route Group لعملاء المتاجر (`/api/stores/{slug}/customers`).
- آخر Regression كامل ناجح للحل (`RetailFlow.IdentitySpike.slnx`): **`47 / 47` Passed** (`IdentityScopeUnitTests` = `14 / 14`، و `IdentitySqlServerIntegrationTests` = `33 / 33`، `0 Failed · 0 Skipped`).
- الخطوة التالية بعد Commit وPush: إضافة `BackOffice` Route Group في `Program.cs`، ثم استكمال `Authentication` و`Authorization` داخل `Program.cs` وبناء Minimal Auth APIs.
- لا يُعد Identity Scope أو Authentication/Authorization مكتملًا إنتاجيًا حتى تنجح اختبارات التكامل السابقة وتُنفذ اختبارات الصلاحيات المطلوبة في المراحل اللاحقة.

### ز. بوابة القبول قبل Phase 07
1. نجاح كل الاختبارات ذات الصلة بـ MVP (`01`–`16`) على SQL Server حقيقي.
2. التأكد من عدم وجود أي مسار Identity يعيد مستخدماً من نطاق مختلف.
3. إرفاق تقرير واضح لسياسة `Email Confirmation` و `Password Reset` و `Lockout`.
4. مراجعة `SQL Migration` وفهارس Identity و `Check Constraint`.
5. تحديث حالة قرارات `ADR` في الوثيقة (`ID-ADR-01`..`05`) إلى `Accepted` أو `Rejected`، وحسم طوبولوجيا رابط المتجر واستراتيجية الجلسات (`AUTH-ADR-01`) أو ترحيلها لـ ADR مستقل.
