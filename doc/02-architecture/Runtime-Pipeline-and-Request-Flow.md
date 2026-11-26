# خط سير التطبيق والطلب

## Startup

1. `WebApplication.CreateBuilder(args)`.
2. `AddProductHostInfrastructure(configuration, environment)` لتسجيل ProblemDetails وOpenAPI وCookie Authentication وSQL `ITicketStore` وAuthorization policies وRateLimiter وHealthChecks وHostFiltering validation.
3. `AddRetailFlowInfrastructure(...)` لتسجيل `RetailFlowDbContext` والتحقق من connection string وخدمات المجال.
4. `Build()` ثم `UseProductHostPipeline()`.
5. Mapping endpoints للمجالات والـ health/OpenAPI/SPA fallback.

## Middleware pipeline كما يرتبه الكود

```text
Forwarded Headers
  → (Production) Exception Handler + HSTS
  → HTTPS Redirection
  → Security Response Headers
  → Rate Limiter
  → Authentication
  → X-CSRF marker check for authenticated unsafe methods
  → Authorization
  → Default Files / Static Files
  → Endpoint execution / SPA fallback
```

يجب الرجوع للكود عند تغيير ترتيب الـ middleware؛ الترتيب جزء من السلوك الأمني.

## Store-scoped request

1. الطلب يأتي بمسار يحتوي `{slug}`.
2. `StoreResolutionEndpointFilter` يقرأ slug وينادي `IStoreDirectoryResolver`.
3. `StoreDirectoryResolver` يطابق slug بعد lower-case trim، ويقبل Store في حالة Active فقط مع Settings وCurrency موجودتين.
4. يتم وضع `StoreRequestContext` في `IStoreRequestContextAccessor` scoped lifetime.
5. Service يستخدم `StoreId` المستخرج من السياق، ويضيف tenant filters في استعلاماته.
6. Endpoints الخاصة بالعميل/التاجر تضيف أيضاً `TenantClaimMatchEndpointFilter` لمقارنة `store_id` claim مع store الحالي.

## ملاحظة أمنية

لا يكفي وجود slug في URL كحدّ أمني. يجب أن يستمر كل query/mutation باستخدام StoreId المقيد، وأن تُختبر محاولات استخدام ID من متجر آخر. توجد اختبارات BOLA/cross-store في `OwaspApiSecurityIntegrationTests` و`PublicStoreCatalogIntegrationTests`.

## Authentication request

- المستخدم يدخل عبر `/api/stores/{slug}/auth/login` أو `/api/platform/auth/login`.
- يتم التحقق من بيانات الدخول بواسطة `IAuthService`.
- `SignInAsync` يبني ClaimsPrincipal (NameIdentifier, Email, Name, Role, optional `store_id`) ويصدر Cookie.
- ASP.NET Core Cookie handler يستخدم `SqlSessionTicketStore` لتخزين AuthenticationTicket في SQL Server.
- الطلبات التالية تتحقق من cookie وتسترجع ticket من SQL store.
- Logout يستدعي `SignOutAsync`، ما يطلب إزالة ticket من ticket store.

## Checkout request

راجع `03-domain/Commerce-Lifecycle.md` و`05-data/Database-Architecture.md`. أهم نقطة: claim الخاص بمفتاح idempotency محفوظ في خطوة SaveChanges أولى، ثم تنفيذ الطلب وتغييرات المخزون والسلة واكتمال claim في SaveChanges ثانية. لا تصف ذلك بأن claim والعملية كلها تتم في transaction واحدة.
