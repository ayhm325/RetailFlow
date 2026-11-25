# توثيق حالة التنفيذ — Phase 06: إثبات المفهوم لتعدد هوية العملاء (Identity Spike)

- **المسار التجريبي:** `experiments/IdentitySpike/`
- **المرجع المعماري:** دراسة إثبات المفهوم لتعدد هوية العملاء (`Phase 06 — v1.0 — 22 نوفمبر 2026`)
- **تاريخ آخر تحديث للتقرير:** `2026-11-25`
- **نقطة التوقف الحالية:** اكتمل سد فجوات `ScopedUserStore` واختبارات `IdentitySpike` السابقة (`49/49`)، واكتملت اختبارات `SessionTransportSpike` بالنتائج: `63/63 Unit` و`19/19 Integration` (`82/82`)، إضافة إلى `4/4 Playwright Browser Tests`، بما في ذلك إغلاق ملاحظات Playwright والتوثيق وتنفيذ `ST-14 — Password Change / Security Stamp / Session Revocation`.
  أثبتت اختبارات `SessionTransportSpike` و`Playwright`:
  - Cross-Store isolation في متصفحين/تبويبين مستقلين.
  - Cross-Site Login وLogout CSRF rejection.
  - Same-Site Cross-Origin Logout rejection مع إرسال Cookie فعلية.
  - بقاء جلسات المتاجر الأخرى سليمة بعد Logout وتغيير كلمة المرور.
  - إبطال جميع جلسات المستخدم ذرّياً عند تغيير كلمة المرور، وإبطال الجلسات عند تغيير `SecurityStamp` بعد انقضاء `ValidationInterval = 5m`.
  - عدم تعديل سجلات `DataProtectionKeys` المشتركة أثناء تشغيل اختبارات Playwright.
  - تنظيف سجلات الاختبار إلى `0` جلسات متبقية في `AuthenticationSessionTickets`.
  بقيت قياس الأداء، وحسم قرارات المنتج/الـ Origin في `AUTH-ADR-01`، وبوابات Production Readiness (`Certificate Rotation`، ونسخ PFX الاحتياطي، واختبار عمليتي OS مستقلتين، وCI).
- **الخطوة التالية:** إجراء قياس الأداء وحسم قرارات `AUTH-ADR-01` المفتوحة واستكمال متطلبات Production Readiness قبل تفعيل Authentication في `IdentitySpike/Program.cs`. يجب إبقاء Authentication غير مفعّل في `IdentitySpike/Program.cs` حتى استكمال بوابات ADR واتخاذ القرار النهائي.

### ملخص حالة المراحل

| المرحلة | الحالة |
| :--- | :--- |
| **0 — Baseline** | الأساس المحلي منجز، وCI متبقٍ |
| **1 — Model** | نموذج Identity الأساسي منجز، وقرارات Profile/Membership متبقية |
| **2 — Scoped Store** | منجزة، بما في ذلك Resolve/Scope Middleware وربط Route Groups الفعلية (`StoreCustomer` و`BackOffice`) |
| **3 — Managers & APIs** | جزئية: Managers وRoute Groups النطاق (`StoreCustomer` و`BackOffice`) منفذة ومختبرة في التطبيق الفعلي؛ Minimal Auth APIs وSignInManager في التطبيق متبقية |
| **4 — Lifecycle** | إثبات الخدمات منجز؛ HTTP endpoints والسياسات متبقية |
| **5 — Security** | لم تبدأ كتنفيذ إنتاجي؛ اختبار ترتيب Authentication مع Scope منفذ في TestServer فقط |
| **6 — SQL/CI** | جزئية: 35 اختبار تكامل ناجحاً في `IdentitySpike` + 19 اختبار تكامل و4 اختبارات متصفح Playwright في `SessionTransportSpike` (تشمل `Sliding Renewal` وتزامن `Renew/Remove` وتنظيف المنتهي و`Shared Data Protection Key Ring` و`Idle 15m / Absolute 8h Expiration` و`403 Authorization Policy` + `ST-14 Password Change / Security Stamp / Session Revocation` + Playwright E2E)؛ CI وتزامن التسجيل وبقية بوابات الإنتاج متبقية |

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
  - حماية عمليات **Lockout** و **Security Stamp** و **خصائص المستخدم** عبر **Hybrid Scope Validation** (`EnsureUserMatchesScopeOrIsNewAsync`): تتحقق من نطاق الكائن في الذاكرة، وتستعلم عن `user.Id` في `AspNetUsers` دون فلتر النطاق — فتسمح بالمستخدم الجديد غير المحفوظ بعد (لدعم `UserManager.CreateAsync`)، وترفض أي كائن يحمل `Id` محفوظاً في متجر/نطاق آخر، مع **Request-Scoped Ownership Cache** داخل `ScopedUserStore` لمنع تكرار استعلامات التحقق (`N+1`) أثناء تدفق `UserManager`/`SignInManager`:
    - **Lockout:** `GetLockoutEndDateAsync`, `SetLockoutEndDateAsync`, `GetAccessFailedCountAsync`, `IncrementAccessFailedCountAsync`, `ResetAccessFailedCountAsync`, `GetLockoutEnabledAsync`, `SetLockoutEnabledAsync`.
    - **Security Stamp:** `GetSecurityStampAsync`, `SetSecurityStampAsync`.
    - **User Properties (`IUserStore`, `IUserEmailStore`, `IUserPasswordStore`):** `GetUserIdAsync`, `GetUserNameAsync`, `SetUserNameAsync`, `GetNormalizedUserNameAsync`, `SetNormalizedUserNameAsync`, `GetEmailAsync`, `SetEmailAsync`, `GetNormalizedEmailAsync`, `SetNormalizedEmailAsync`, `GetEmailConfirmedAsync`, `SetEmailConfirmedAsync`, `GetPasswordHashAsync`, `SetPasswordHashAsync`, `HasPasswordAsync`.
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
  - إضافة `UseRouting()` صراحةً ثم `UseMiddleware<IdentityScopeMiddleware>()` بعد `UseHttpsRedirection()`، وربط Route Group `/api/stores/{slug}/customers` بـ `IdentityScopeMetadata(AccountKind.StoreCustomer)` وRoute Group `/api/backoffice` بـ `IdentityScopeMetadata(AccountKind.BackOffice)` مع نقطتي `/scope` للتحقق؛ لم تتم إضافة `UseAuthentication()` أو `UseAuthorization()` إلى التطبيق الفعلي بعد.

---

### د. الاختبارات المكتوبة حتى الآن
1. **اختبارات الوحدة (`IdentityScopeUnitTests` — 14 اختباراً):**
   - `IdentityLookupScopeTests.cs` (3 اختبارات).
   - [`IdentityLookupScopeAccessorTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentityScopeUnitTests/Infrastructure/Identity/IdentityLookupScopeAccessorTests.cs) (5 اختبارات شاملة رفض التعيين المكرر `SetCurrent` والتحقق من نص الخطأ والحفاظ على مرجع النطاق الأصلي).
   - `UserStoreCapabilitiesTests.cs` (اختبار واحد يتحقق من حصر قدرات `UserManager` في قدرات MVP فقط).
   - [`IdentityScopeResolverContractTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentityScopeUnitTests/Infrastructure/Identity/IdentityScopeResolverContractTests.cs) (5 حالات: رفض slug الفارغ/المسافات، واحترام `CancellationToken`).
2. **اختبارات التكامل مع SQL Server وHTTP TestServer (`IdentitySqlServerIntegrationTests` — 35 اختباراً):**
   - [`TestAssemblyConfiguration.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/TestAssemblyConfiguration.cs): تعطيل التوازي (`DisableTestParallelization = true`) لتجنب Deadlocks عند مشاركة قاعدة بيانات واحدة بين مجموعات الاختبار.
   - `ScopedUserStoreLookupTests.cs` (اختباران): عزل `Users` و `FindById/Email/Name`، وعزل `BackOffice`، واختبار Fail-Closed.
   - `ScopedUserStoreClaimsTests.cs` (اختباران): منع قراءة/إضافة/استبدال/حذف Claims لمتجر آخر أو لكائن مزور، واختبار `UserManagerGetUsersForClaimAsync_ReturnsOnlyUsersFromCurrentStore` الذي يثبت أن `UserManager.GetUsersForClaimAsync` لا يعيد مستخدمين من متجر آخر رغم تطابق الـ Claim.
   - `ScopedUserStoreLoginTests.cs` (اختبار واحد): عزل عمليات `IUserLoginStore` ومنع الكائنات المزورة واختبار Fail-Closed.
   - `ScopedUserStoreSecurityStateTests.cs` (اختبار واحد): عزل عمليات `IUserLockoutStore` و `IUserSecurityStampStore` ورفض الكائنات المزورة.
   - [`ScopedUserStoreUserPropertiesTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/ScopedUserStoreUserPropertiesTests.cs) (اختبار واحد): عزل قراءة وتعديل خصائص المستخدم (`UserId`, `UserName`, `Email`, `PasswordHash`, إلخ) عبر `ScopedUserStoreFacade` ورفض الكائنات المزورة عبر المتاجر.
   - `ScopedUserStoreTokenTests.cs` (اختبار واحد): عزل عمليات `IUserAuthenticationTokenStore` ورفض الكائنات المزورة.
   - `UserManagerStoreScopingTests.cs` (اختبار واحد): السماح بنفس البريد الإلكتروني في متجرين مختلفين ورفض التكرار داخل نفس المتجر.
   - [`UserManagerLifecycleTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/UserManagerLifecycleTests.cs) (4 اختبارات): تأكيد البريد، إعادة تعيين كلمة المرور وإبطال التوكن السابق، Lockout وانتهاء مدته، والتحقق من `CheckPasswordSignInAsync`.
   - [`IdentityScopeResolverTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeResolverTests.cs) (اختبار SQL واحد): يحل المتجر النشط فقط، ويرفض غير النشط وغير الموجود، ويتحقق من slug بأحرف كبيرة.
   - [`IdentityScopeMiddlewareTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeMiddlewareTests.cs) (11 اختباراً عبر `TestServer` و `DefaultHttpContext`): StoreCustomer، BackOffice، Endpoint دون Metadata (عبر `TestServer` وعبر `DefaultHttpContext` + `SpyIdentityScopeResolver`)، Store غير قابل للحل وإرجاع 404، عزل Scope بين طلبين متتاليين، و5 حالات `MissingOrInvalidSlug` (غياب المفتاح، `null`، `""`، `" "`، ونوع غير `string` مثل `123` مع إثبات عدم استدعاء Resolver وعدم ضبط Scope).
   - [`IdentityScopeAuthenticationOrderingTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeAuthenticationOrderingTests.cs) (اختبار واحد عبر `TestServer`): يثبت أن Fake Authentication Handler يرى Scope بعد أن يضبطه Middleware في Pipeline صريح الترتيب.
   - [`IdentitySpikeApiFactory.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentitySpikeApiFactory.cs) و `IdentitySpikeApiFactoryTests.cs` (اختباران باستخدام `WebApplicationFactory`): تشغيل API الفعلي داخل TestServer والتحقق من اتصال قاعدة البيانات، والتحقق من تسجيل Resolver وAccessor كـ Scoped عبر DI scopes مستقلة.
   - [`IdentityScopeRouteGroupIntegrationTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityScopeRouteGroupIntegrationTests.cs) (5 اختبارات باستخدام `WebApplicationFactory` وSQL Server الفعلي): حل المتجر النشط عبر `/api/stores/{slug}/customers/scope`، إرجاع `404` للمتجر غير الموجود وغير النشط، عزل Scope لمتجرين متزامنين، واستقلال الطلبات المتعاقبة (`A → B → A`).
   - [`IdentityLookupScopeAccessorLifetimeTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/IdentityLookupScopeAccessorLifetimeTests.cs) و [`AccessorInstanceTracker.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/AccessorInstanceTracker.cs) (اختبار واحد): إثبات إنشاء مثيل مستقل من `IIdentityLookupScopeAccessor` لكل طلب HTTP (`Assert.NotSame`) واحتفاظ كل مثيل بحالة متجره عبر SQL Server الفعلي.
   - [`BackOfficeRouteGroupIntegrationTests.cs`](../experiments/IdentitySpike/tests/RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests/Infrastructure/Identity/BackOfficeRouteGroupIntegrationTests.cs) (اختبار واحد باستخدام `WebApplicationFactory` مقابل `Program.cs` الفعلي): إثبات تطبيق Metadata وMiddleware وإنشاء `BackOffice` Identity Scope العالمي، مع استخدام `SpyIdentityScopeResolver` للتحقق من عدم حدوث أي استعلام متجر (لا يلزم استعلام SQL لنطاق `BackOffice`).

3. **تجربة نقل الجلسات (`experiments/SessionTransportSpike/` — 82 اختباراً + 4 Playwright):**
   - `RetailFlow.SessionTransportSpike.UnitTests` (63 اختباراً): `StoreCustomerCookieManagerTests.cs` (7 اختبارات)، `CanonicalStoreSlugMiddlewareTests.cs` (5 حالات)، `BrowserCsrfProtectionMiddlewareTests.cs` (37 حالة)، `BrowserCsrfProtectionOptionsValidatorTests.cs` (8 حالات للتحقق من `Development`/`Staging`/`Production`)، و`StoreCustomerScopeAuthorizationHandlerTests.cs` (6 حالات).
   - `RetailFlow.SessionTransportSpike.IntegrationTests` (19 اختباراً على SQL Server و`WebApplicationFactory` و`FakeTimeProvider` عبر `Microsoft.Extensions.TimeProvider.Testing 10.0.0` دون أي تحذيرات بناء): الاختبارات الـ 15 السابقة إضافةً إلى 4 اختبارات `ST-14` (`ChangePassword_RevokesAllUserSessions_AndRequiresNewPassword`، و`ChangePassword_FailedValidation_DoesNotRevokeSessions`، و`SecurityStampChange_InvalidatesSessionsAtNextValidationInterval`، و`ChangePassword_WithCrossStoreCookie_ReturnsForbidden_AndPreservesStoreASession`).
   - **تخزين الجلسات والإبطال الخادمي وShared Key Ring و403 Policy:** تنفيذ `PersistedAuthenticationTicket` و`SessionTicketDbContext` (`AuthenticationSessionTickets` و`DataProtectionKeys` عبر `IDataProtectionKeyContext` وحزمة `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore 10.0.15`) و`SessionTicketDbContextFactory` (`__EFMigrationsHistory_SessionTickets`) و`SessionLifetimeOptions` (`IdleTimeout = 15m`, `AbsoluteLifetime = 8h`) و`SqlServerTicketStore` (بحماية `IDataProtector` ومفتاح عشوائي `32 bytes`، وحقن `TimeProvider` وتمريره إلى `CookieAuthenticationOptions.TimeProvider`، وحساب `AbsoluteExpiresUtc` مرة واحدة وتقييد `Sliding Renewal` به دون تغييره، ومنع `Stale Renewal` وإحياء جلسة محذوفة)، وإضافة `ExpiredSessionTicketCleanupService` (`BackgroundService` بفاصل 5 دقائق لإزالة الجلسات المنتهية وفق كلا الحدين عبر `ExecuteDeleteAsync`)، وإعداد `ApplicationName = "RetailFlow.Auth"` وحماية X.509 عبر `DataProtectionCertificateLoader` (`EphemeralKeySet` + `ProtectKeysWithCertificate` + `UnprotectKeysWithAnyCertificate`)، وفصل `403 Authorization Policy` (`RetailFlowAuthorizationPolicies.StoreCustomerScope` + `StoreCustomerScopeRequirement` + `StoreCustomerScopeAuthorizationHandler` بعمر `Scoped` على `/me` و`/logout`) مع حارس `SecurityStampValidator` في `StoreCustomerCookieAuthenticationEvents` عند اختلاف `StoreId` عن Scope الطلب، وتطبيق Migrations `20261124225416_CreateAuthenticationSessionTickets` و`20261125063008_AddDataProtectionKeys` و`20261125072108_AddAbsoluteSessionExpiration` محلياً على `RetailFlowIdentitySpike`، وإضافة حارس `409 Conflict` لتبديل الحساب في `POST /login`، مع تنظيف `AuthenticationSessionTickets` في `RemoveTestDataAsync`.
   - **تحسينات مطابقة التوثيق الرسمي المطبقة:** `RedirectToLogin` -> `401` و `RedirectToAccessDenied` -> `403` لـ SPA/API، حماية CSRF عبر `BrowserCsrfProtectionMiddleware` (`X-RetailFlow-Request: 1` + Strict `Origin/Referer` مقابل `Security:BrowserCsrf:BrowserOrigin` + `Sec-Fetch-Site` دون CORS، مع قصر `http://` على Loopback في `Development` فقط و`Cache-Control: no-store` عند الرفض، بعد إزالة Synchronizer Antiforgery Token)، ضبط `SecurityStampValidatorOptions.ValidationInterval = 5m` (< عمر الكوكي `15m`)، وموازنة زمن `POST /login` عبر Dummy Hash عند غياب المستخدم.

> **نتائج التشغيل الفعلي:**
> - **`IdentityScopeUnitTests`: `14 / 14` Passed، `0 Failed · 0 Skipped`.**
> - **`IdentitySqlServerIntegrationTests`: `35 / 35` Passed، `0 Failed · 0 Skipped`.** وتشمل اختبارات SQL Server الفعلية واختبارات HTTP باستخدام `TestServer` و `WebApplicationFactory`.
> - **Full Solution Regression (`RetailFlow.IdentitySpike.slnx`): `49 / 49` Passed (`0 Failed · 0 Skipped`).**
> - **Full Solution (`RetailFlow.SessionTransportSpike.slnx` بتاريخ `2026-11-25`): `82 / 82` Passed (`63` Unit + `19` Integration، `0 Failed · 0 Skipped`).**
> - **Playwright Browser Acceptance (`RetailFlow.SessionTransportSpike.Playwright.slnx`): `4 / 4` Passed (`0 Failed · 0 Skipped`).**
> - اختبار API Factory نفذ `SELECT 1` على قاعدة SQL Server.
> - تحذير `Failed to determine the https port for redirect` ظهر في `TestServer` لأن الاختبار لا يهيئ منفذ HTTPS؛ لم يمنع اختبار الاتصال أو نجاح الاختبارات، لكنه لا يثبت سلوك HTTPS Redirection.

### هـ. حدود ما تم إثباته حتى الآن
- `Email Confirmation` مثبت على مستوى الخدمة (`UserManager`)، وليس إرسال البريد فعلياً.
- `Password Reset` مثبت على مستوى الخدمة (`UserManager`)، ولم ننفذ `Forgot Password Endpoint` بعد.
- `Lockout` مثبت على مستوى الخدمة (`UserManager` و `SignInManager.CheckPasswordSignInAsync`)، لكن عزل حسابين بين متجرين عند فشل الدخول لم يُختبر بعد.
- `SecurityStamp` تم التحقق من تغييره وحفظه في SQL Server وإبطال Reset Token السابق.
- `Claims`: تم إثبات عزل عمليات Claims المرتبطة بمستخدم محدد، وكذلك مسار البحث `UserManager.GetUsersForClaimAsync` باستخدام SQL Server الفعلي عبر `ScopedUserStoreFacade` و `ScopedUserStore`.
- `Data Protection`: تم تنفيذ `Shared SQL Key Ring` في `SessionTransportSpike` (`DataProtectionKeys` + شهادة X.509 + `ApplicationName = "RetailFlow.Auth"`) وإثباته في اختبارات `A/B/C` و`DifferentApplicationName` و`WrongCertificate`؛ أما `IdentitySpike` نفسه فيبقي `AddDataProtection()` أساسياً دون ربط Key Ring المشترك بعد، كما يتبقى قبل الإنتاج تدوير الشهادة (`Certificate Rotation`) وإجراء النسخ الاحتياطي لـ PFX واختبار عمليتي OS مستقلتين.
- اختبارات `IdentityScopeAuthenticationOrderingTests` تستخدم `TestServer` وFake Authentication Handler؛ وهي تثبت الترتيب الصريح في الـ Pipeline الاختباري.
- اختبارات `StoreCustomer Route Group` (`IdentityScopeRouteGroupIntegrationTests`) و `Accessor Lifetime` (`IdentityLookupScopeAccessorLifetimeTests`): تثبت التكامل عبر `WebApplicationFactory` مع SQL Server الفعلي.
- اختبار `BackOffice Route Group` (`BackOfficeRouteGroupIntegrationTests`): يعمل عبر `WebApplicationFactory` مقابل `Program.cs` الفعلي مع `SpyIdentityScopeResolver` للتحقق من عدم حدوث أي استعلام متجر (لا يلزم استعلام SQL لنطاق `BackOffice`).
- `UseAuthentication()` و`UseAuthorization()` لم تتم إضافتهما إلى الـ Pipeline الفعلي في `IdentitySpike/Program.cs` بعد (مختبرة في `SessionTransportSpike` بانتظار إغلاق بوابات `AUTH-ADR-01` المتبقية مثل اختبارات Playwright وقياس الأداء ومتطلبات Production Readiness).
- اختبار `IdentityScopeAuthenticationOrderingTests` لا يثبت سلوك الإضافة التلقائية للـ Middleware بواسطة `WebApplication`؛ بل يثبت الترتيب الصريح الذي سنعتمده عند تفعيل Authentication.

---

## 2. ما تبقى لإنهاء Phase 06

### أ. بقايا المراحل 0 و 1
- **المرحلة 0 (Baseline):** إعداد خط CI يطابق إصدارات `.NET 10` واختبارات SQL Server، وتوثيق Commit SHA النهائي للتغييرات الحالية *(الـ Baseline Commit موجود مسبقاً)*.
- **المرحلة 1 (Model):** إضافة `CustomerProfile` (مع المفتاح البديل `(StoreId, Id)`) و `StoreMembership` إذا تقرر تضمينها في الـ Spike، أو تثبيتها قبل الانتقال لـ Phase 07.
- *(ملاحظة: تم إكمال تغطية `IUserLoginStore` بالكامل وتنظيف ملفات القالب `UnitTest1.cs` و `/weatherforecast`.)*

### ب. المرحلة 3 — Managers & Minimal APIs (الخطوة القادمة مباشرة)
- *(منجز على مستوى `UserManager`/`SignInManager` وRoute Groups: إنشاء المستخدمين بنطاق المتجر عبر `UserManager.CreateAsync`، والتحقق من كلمة المرور مع Lockout عبر `SignInManager.CheckPasswordSignInAsync`، وربط واختبار Route Groups لكل من `StoreCustomer` و`BackOffice` في `Program.cs`.)*
- **المتبقي:**
  - حسم استراتيجية `Cookies` مقابل `Standard JWT` في `AUTH-ADR-01` قبل تنفيذ `Authentication`.
  - إضافة/تهيئة `AddAuthentication()` و `.AddSignInManager()` في `Program.cs`، وإضافة `AddHttpContextAccessor()` فقط إذا أثبت تصميم الخدمات حاجته إليه. كما يجب وضع `UseAuthentication()` و`UseAuthorization()` صراحة بعد `IdentityScopeMiddleware` واختبار الترتيب داخل التطبيق الفعلي.
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
- `StoreCustomer Route Group`: Implemented and tested.
- `BackOffice Route Group`: Implemented and tested.
- `IdentityScopeMiddleware`: Tested.
- `IdentityLookupScopeAccessor lifecycle`: Verified.
- آخر Regression كامل ناجح لحل `IdentitySpike` (`RetailFlow.IdentitySpike.slnx`): **`49 / 49` Passed** (`IdentityScopeUnitTests` = `14 / 14`، و `IdentitySqlServerIntegrationTests` = `35 / 35`، `0 Failed · 0 Skipped`).
- آخر تشغيل ناجح لحل `SessionTransportSpike` (`RetailFlow.SessionTransportSpike.slnx`): **`78 / 78` Passed** (`UnitTests` = `63 / 63`، و `IntegrationTests` = `15 / 15`، `0 Failed · 0 Skipped`).
- **Next:** استكمال بوابات `AUTH-ADR-01` المتبقية (اختبارات المتصفح بـ Playwright، واختبارات الأداء، ومتطلبات Production Readiness لـ Data Protection).
- لا يُعد Identity Scope أو Authentication/Authorization مكتملًا إنتاجيًا حتى تنجح اختبارات التكامل السابقة وتُنفذ اختبارات الصلاحيات المطلوبة في المراحل اللاحقة.

### ز. بوابة القبول قبل Phase 07
1. نجاح كل الاختبارات ذات الصلة بـ MVP (`01`–`16`) على SQL Server حقيقي.
2. التأكد من عدم وجود أي مسار Identity يعيد مستخدماً من نطاق مختلف.
3. إرفاق تقرير واضح لسياسة `Email Confirmation` و `Password Reset` و `Lockout`.
4. مراجعة `SQL Migration` وفهارس Identity و `Check Constraint`.
5. تحديث حالة قرارات `ADR` في الوثيقة (`ID-ADR-01`..`05`) إلى `Accepted` أو `Rejected`، وحسم طوبولوجيا رابط المتجر واستراتيجية الجلسات (`AUTH-ADR-01`) أو ترحيلها لـ ADR مستقل.
