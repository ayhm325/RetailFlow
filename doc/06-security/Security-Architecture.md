# معمارية الأمن والهوية

**نطاق الوثيقة:** Product Host الأساسي في `src/` عند baseline. مشاريع التجارب منفصلة.

## 1. Authentication

- Store users ينتمون إلى `StoreId` ويستخدمون Customer أو MerchantOwner.
- PlatformAdmin مستخدم global حيث StoreId null.
- `AuthService` يبحث عن المستخدم باستخدام `StoreId + NormalizedEmail` أو منصة بدون StoreId.
- كلمة المرور تُخزّن كـ PBKDF2-SHA256 encoded string مع version/iteration/salt/hash.
- Cookie Authentication يصدر principal يحتوي NameIdentifier, Email, Name, Role و`store_id` عند وجوده.
- SQL ticket store يستخدم `ITicketStore`.

**CODE:** `AppUser.cs`, `AuthService.cs`, `PasswordHasherService.cs`, `AuthEndpoints.cs`, `SqlSessionTicketStore.cs`.

## 2. Cookie configuration

| Property | Value in Product Host |
|---|---|
| Name | `__Host-RetailFlow.Session` |
| HttpOnly | true |
| SecurePolicy | Always |
| SameSite | Lax |
| Path | `/` |
| Domain | غير مضبوط في الكود المعاين، وهو مناسب لشرط `__Host-` |
| SlidingExpiration | true |
| ExpireTimeSpan | 12 hours |
| Ticket storage | SQL Server عبر `ITicketStore` |

لا تعني هذه القيم أن إدارة الجلسات كاملة؛ راجع فجوات rotation/revocation/cleanup/Data Protection في `10-open-items`.

## 3. Authorization and tenant boundary

- Policies مسجلة: Customer, MerchantOwner, PlatformAdmin.
- Customer endpoints تتطلب Customer ثم resolve store ثم match `store_id` claim.
- Merchant endpoints تتطلب MerchantOwner ثم resolve store ثم match `store_id` claim.
- Platform endpoints تتطلب PlatformAdmin.
- الخدمات نفسها تقيد البيانات باستخدام StoreId.

يجب إجراء مراجعة BOLA لكل endpoint يتعامل مع GUID من العميل، وعدم الاكتفاء بوجود role claim.

## 4. CSRF behavior الملحوظ

يوجد middleware عام بعد Authentication وقبل Authorization. لكل method غير GET/HEAD/OPTIONS، إذا كان المستخدم authenticated، يجب أن يكون Header `X-CSRF` مساوياً حرفياً `1` وإلا يُرفض الطلب بـ 403.

هذه ليست قيمة synchronizer token متغيرة أو cryptographically bound token كما في بعض الأنماط المعيارية. كما أن Product Host code لا يظهر مقارنة Origin/Referer في هذا middleware. يجب تقييم هذا التصميم بالاشتراك مع سياسة CORS والـ browser deployment model، واختبار login/register CSRF وسيناريوهات cross-origin. وجود `SameSite=Lax` طبقة دفاع إضافية وليس بديلاً عن CSRF defense موثوق.

## 5. Rate limiting

- Global fixed-window rate limiter.
- Defaults: PermitLimit=120، WindowSeconds=60، QueueLimit=0.
- `/health*` معفى.
- Requests ذات مسار `/api/stores/{slug}` تقسّم على slug + IP؛ بقية الطلبات على IP.
- Status 429 مع Retry-After إذا توفر metadata وProblemDetails.

لا توجد policy منفصلة موثقة لكل login/register/checkout؛ يُنصح بمراجعة حماية credential stuffing وabuse للعمليات الحساسة.

## 6. HTTP hardening

- Forwarded Headers قبل HTTPS/HSTS.
- في Production: ExceptionHandler وHSTS.
- HTTPS Redirection.
- Headers: `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`.
- Host filtering يتطلب `AllowedHosts` صريحاً خارج Development ويرفض wildcard/قيم مفتوحة.
- OpenAPI mapped في Development فقط.

## 7. Password hashing review

التنفيذ الحالي يستخدم PBKDF2-HMAC-SHA256 بـ 310,000 iterations، 16-byte salt، 32-byte output، وFixedTimeEquals للمقارنة. تعليق الكود يصفها OWASP-compliant. لكن OWASP Password Storage Cheat Sheet الحالية توصي في سياق FIPS-140 بـ PBKDF2-HMAC-SHA256 بعدد 600,000 iterations أو أكثر. لا يكفي التعليق لإثبات التوافق؛ راجع متطلبات الامتثال وbenchmarking وقيود الترقية، وأضف versioned rehash strategy.

## 8. البيانات والجلسات

`SessionTickets.Value` تخزن serialized ticket في SQL Server. لا يظهر في `SqlSessionTicketStore` تشفير إضافي مخصص لقيمة العمود. احمِ database access والنسخ الاحتياطية، وراجع حماية Data Protection keys. لا يوجد cleanup worker ظاهر في Product Host للسجلات المنتهية.

## 9. نطاق غير مثبت

لم يظهر في Product Host: MFA، password reset، email verification، account lockout/risk-based throttling، session listing/revoke device، refresh token، payment gateway security، audit logging مركزي، CSP/Permissions-Policy، أو سياسة CORS صريحة.
