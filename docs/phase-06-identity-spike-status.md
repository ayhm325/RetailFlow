# توثيق حالة التنفيذ — Phase 06: إثبات المفهوم لتعدد هوية العملاء (Identity Spike)

- **المسار التجريبي:** `experiments/IdentitySpike/`
- **المرجع المعماري:** دراسة إثبات المفهوم لتعدد هوية العملاء (`Phase 06 — v1.0 — 22 نوفمبر 2026`)
- **تاريخ آخر تحديث للتقرير:** `2026-11-24`
- **الحالة العامة:** **المرحلة 2 منجزة**، والمشروع جاهز للانتقال إلى **المرحلة 3 (Managers & Minimal APIs)**.

---

## 1. ملخص ما تم إنجازه من خطة Phase 06

### أ. المرحلة 0 — Baseline (إعداد الحل وتثبيت الإصدارات)
- تثبيت إصدار .NET SDK في [`global.json`](../global.json) (`10.0.404`, `rollForward: latestPatch`).
- تثبيت أداة `dotnet-ef` محلياً في [`experiments/IdentitySpike/dotnet-tools.json`](../experiments/IdentitySpike/dotnet-tools.json) بالإصدار `10.0.15`.
- إنشاء ملف الحل [`RetailFlow.IdentitySpike.slnx`](../experiments/IdentitySpike/RetailFlow.IdentitySpike.slnx) ويضم:
  - مشروع الـ API: `RetailFlow.IdentitySpike.Api` (`net10.0`)
  - مشروع اختبارات الوحدة: `RetailFlow.IdentitySpike.IdentityScopeUnitTests`
  - مشروع اختبارات التكامل: `RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests`
- تثبيت حزم `Microsoft.AspNetCore.Identity.EntityFrameworkCore` و `Microsoft.EntityFrameworkCore.SqlServer` و `Microsoft.EntityFrameworkCore.Design` و `Microsoft.AspNetCore.OpenApi` و `Microsoft.AspNetCore.Mvc.Testing` عند الإصدار `10.0.15`، مع `xUnit 2.9.3`.
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
- **التسجيل الأولي في [`Program.cs`](../experiments/IdentitySpike/src/RetailFlow.IdentitySpike.Api/Program.cs):**
  - تسجيل `IIdentityLookupScopeAccessor` كـ `Scoped`.
  - تسجيل `AddIdentityCore<ApplicationUser>` مع `options.User.RequireUniqueEmail = false` و `.AddUserStore<ScopedUserStore>()`.
  - إضافة نقطة فحص اتصال قاعدة البيانات `GET /diagnostics/database` في بيئة التطوير.

---

### د. الاختبارات المكتوبة حتى الآن
1. **اختبارات الوحدة (`IdentityScopeUnitTests`):**
   - [`IdentityLookupScopeTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentityScopeUnitTests/Infrastructure/Identity/IdentityLookupScopeTests.cs) (3 اختبارات).
   - [`IdentityLookupScopeAccessorTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentityScopeUnitTests/Infrastructure/Identity/IdentityLookupScopeAccessorTests.cs) (5 اختبارات).
2. **اختبارات التكامل مع SQL Server (`IdentitySqlServerIntegrationTests`):**
   - [`ScopedUserStoreLookupTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/ScopedUserStoreLookupTests.cs): عزل `Users` و `FindById/Email/Name`، وعزل `BackOffice`، واختبار Fail-Closed.
   - [`ScopedUserStoreClaimsTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/ScopedUserStoreClaimsTests.cs): منع قراءة/إضافة/استبدال/حذف Claims لمتجر آخر أو لكائن مزور.
   - [`ScopedUserStoreLoginTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/ScopedUserStoreLoginTests.cs): عزل عمليات `IUserLoginStore` (`AddLoginAsync`, `GetLoginsAsync`, `FindByLoginAsync`, `RemoveLoginAsync`)، ومنع الكائنات المزورة، واختبار Fail-Closed.
   - [`ScopedUserStoreSecurityStateTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/ScopedUserStoreSecurityStateTests.cs): عزل عمليات `IUserLockoutStore` و `IUserSecurityStampStore` ورفض الكائنات المزورة.
   - [`ScopedUserStoreTokenTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/ScopedUserStoreTokenTests.cs): عزل عمليات `IUserAuthenticationTokenStore` ورفض الكائنات المزورة.
   - [`UserManagerStoreScopingTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/UserManagerStoreScopingTests.cs): اختبار `UserManager.CreateAsync` للسماح بنفس البريد الإلكتروني في متجرين مختلفين ورفض التكرار داخل نفس المتجر.

> **نتائج التشغيل الفعلي للاختبارات (تم التحقق منها بتاريخ `2026-11-24` بعد إكمال `IUserLoginStore` وتنظيف ملفات القالب):**
> - **`IdentityScopeUnitTests`**: نجحت جميع الاختبارات (**Passed: 8 / Total: 8**) — الزمن: `2.8376` ثانية عبر `dotnet test --no-restore`.
> - **`IdentitySqlServerIntegrationTests` (مقابل SQL Server الفعلي):** نجحت جميع الاختبارات (**Passed: 7 / Total: 7**) — الزمن: `4.8608` ثانية عبر `dotnet test --no-restore`:
>   - `ScopedUserStoreLookupTests.CustomerLookups_AreRestrictedToCurrentStore_AndBackOfficeRemainsGlobal` — `Passed` (`1 s`)
>   - `ScopedUserStoreLookupTests.Users_WithoutIdentityLookupScope_FailClosed` — `Passed` (`132 ms`)
>   - `ScopedUserStoreClaimsTests.ClaimsOperations_AreRestrictedToCurrentStore` — `Passed` (`1 s`)
>   - `ScopedUserStoreLoginTests.LoginOperations_AreRestrictedToCurrentStore_AndFailClosedWithoutScope` — `Passed` (`1 s`)
>   - `ScopedUserStoreSecurityStateTests.LockoutAndSecurityStamp_AreRestrictedToCurrentStore` — `Passed` (`1 s`)
>   - `ScopedUserStoreTokenTests.TokenOperations_AreRestrictedToCurrentStore` — `Passed` (`1 s`)
>   - `UserManagerStoreScopingTests.CreateAsync_AllowsSameEmailAcrossStores_AndRejectsDuplicateWithinStore` — `Passed` (`1 s`)

---

## 2. ما تبقى لإنهاء Phase 06

### أ. بقايا المراحل 0 و 1
- **المرحلة 0 (Baseline):** عمل أول Git Commit للمشروع، وإعداد خط CI يطابق إصدارات `.NET 10` واختبارات SQL Server.
- **المرحلة 1 (Model):** إضافة `CustomerProfile` (مع المفتاح البديل `(StoreId, Id)`) و `StoreMembership` إذا تقرر تضمينها في الـ Spike، أو تثبيتها قبل الانتقال لـ Phase 07.
- *(ملاحظة: تم إكمال تغطية `IUserLoginStore` بالكامل وتنظيف ملفات القالب `UnitTest1.cs` و `/weatherforecast`.)*

### ب. المرحلة 3 — Managers & Minimal APIs (الخطوة القادمة مباشرة)
- تهيئة `SignInManager<ApplicationUser>` (مع نظام مصادقة مناسب للـ Spike، مع إبقاء القرار النهائي بين `Cookies` و `JWT` لـ `AUTH-ADR-01`).
- بناء آلية ربط النطاق بالطلب (`IdentityScopeResolver` عبر Middleware أو Endpoint Filter) لتعيين `IdentityLookupScope` تلقائياً من `{slug}` للعملاء أو من مسار `BackOffice` مع Fail-Closed عند غياب النطاق.
- تنفيذ الـ Endpoints التالية:
  - `POST /api/stores/{slug}/customers/register`:
    - حل `StoreId` من `{slug}` على الخادم والتحقق من أن المتجر نشط.
    - توليد `UserName` داخلي عشوائي وفريد عالمياً.
    - إنشاء المستخدم عبر `UserManager.CreateAsync(user, password)` ومعالجة أخطاء التزامن (`2601/2627` على الفهرس المحدد) بشكل آمن.
  - `POST /api/stores/{slug}/customers/login`:
    - جلب المستخدم أولاً عبر `userManager.FindByEmailAsync(request.Email)` (المقيد بالنطاق) ثم استدعاء `signInManager.PasswordSignInAsync(user, request.Password, ...)` برسالة فشل عامة موحدة.
- تغطية سيناريوهات الاختبار **`01`** و **`02`** و **`04`** من مصفوفة Phase 06 (تسجيل ودخول بنفس البريد في متجرين مستقلين، ورفض التكرار في نفس المتجر).

### ج. المرحلة 4 — Lifecycle (دورة حياة الحساب)
- تنفيذ واختبار `POST /api/stores/{slug}/customers/confirm-email` (سيناريو **`11`**: رفض تأكيد حساب متجر A عبر Slug متجر B).
- تنفيذ واختبار `POST /api/stores/{slug}/customers/forgot-password` و `reset-password` (سيناريو **`10`** و **`15`**: استجابة موحدة، Token مرتبط بالمتجر، منع إعادة الاستخدام).
- اختبار `GetUserAsync` و `Lockout` الفعلي عبر `SignInManager` (سيناريوهات **`08`** و **`09`**: قفل حساب A لا يقفل حساب B).
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

### و. بوابة القبول قبل Phase 07
1. نجاح كل الاختبارات ذات الصلة بـ MVP (`01`–`16`) على SQL Server حقيقي.
2. التأكد من عدم وجود أي مسار Identity يعيد مستخدماً من نطاق مختلف.
3. إرفاق تقرير واضح لسياسة `Email Confirmation` و `Password Reset` و `Lockout`.
4. مراجعة `SQL Migration` وفهارس Identity و `Check Constraint`.
5. تحديث حالة قرارات `ADR` في الوثيقة (`ID-ADR-01`..`05`) إلى `Accepted` أو `Rejected`، وحسم طوبولوجيا رابط المتجر واستراتيجية الجلسات (`AUTH-ADR-01`) أو ترحيلها لـ ADR مستقل.
