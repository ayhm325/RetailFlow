# معمارية الأمن والهوية

**نطاق الوثيقة:** Product Host الأساسي في `src/` عند baseline. مشاريع التجارب منفصلة.

## 1. Authentication

- Store users ينتمون إلى `StoreId` ويستخدمون Customer أو MerchantOwner.
- PlatformAdmin مستخدم global حيث StoreId null.
- `AuthService` يبحث عن المستخدم باستخدام `StoreId + NormalizedEmail` أو منصة بدون StoreId.
- كلمة المرور تُخزّن عبر `RetailFlowPasswordHasher` بصيغة ASP.NET Core Identity V3 (PBKDF2-HMAC-SHA512 بـ `220,000` تكرار) مع توافق التجزئات القديمة `v1.` والترقية التلقائية عند الدخول، وتطبيع Unicode NFC.
- سياسة كلمات المرور المركزية (`DependencyInjection.cs` + `RetailFlowPasswordValidator.cs`) متوافقة مع إرشادات NIST SP 800-63B-4: حد أدنى `15` حرفاً وحد أقصى `1024` حرفاً، دون قيود تركيب أحرف مصطنعة، مع السماح بالمسافات وUnicode، وحظر كلمات المرور الشائعة/المخترقة ومطابقات البريد والاسم والأنماط المتكررة، ودون انتهاء صلاحية دوري تعسفي، مع حماية قفل الحساب (`5` محاولات / `15` دقيقة).
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
| SlidingExpiration | true (مقيد بالمهلة المطلقة `AbsoluteExpiresAtUtc`) |
| ExpireTimeSpan | idle/absolute حسب الدور: `PlatformAdmin` (`1h`/`8h`)، `MerchantOwner` (`4h`/`24h`)، `Customer` (`12h`/`7d`) |
| Ticket storage | SQL Server عبر `SqlSessionTicketStore` (`CreatedAtUtc`, `AbsoluteExpiresAtUtc`, `ExpiresAtUtc`, `LastSeenAtUtc`) |

لا تعني هذه القيم أن إدارة الجلسات كاملة؛ راجع فجوات rotation/revocation/cleanup/Data Protection في `10-open-items`.

## 3. Authorization and tenant boundary

- Policies مسجلة: `Customer`, `MerchantOwner`, `PlatformAdminEnrollment` (يتطلب دور `PlatformAdmin` فقط لإتمام التسجيل الأولي لـ MFA)، و`PlatformAdmin` (يتطلب دور `PlatformAdmin` + `mfa_enrolled=true` + `amr=mfa` عند تفعيل `PlatformSecurity:RequireAdminMfa`).
- فصل **حالة الحساب** عن **مستوى توثيق الجلسة**: `RetailFlowClaimsPrincipalFactory` يصدر `mfa_enrolled` فقط من حالة قاعدة البيانات (`TwoFactorEnabled` وحالة `Enabled` أو `ReplacementPending`) ولا يصدر `amr=mfa` تلقائياً؛ يُصدر `amr=mfa` فقط عبر `AuthService.SignInSessionAsync` بعد نجاح TOTP أو Recovery Code أو تفعيل MFA، ويحافظ `SecurityStampValidatorOptions.OnRefreshingPrincipal` (`ValidationInterval = TimeSpan.Zero`) على `amr=mfa` فقط إذا كانت الجلسة الأصلية تحمله بالفعل وبقي الحساب مسجلاً بـ MFA.
- تفعيل MFA أو استبدال أداة المصادقة أو تعطيلها أو إعادة توليد Recovery Codes يحدّث `SecurityStamp` ويبطل بقية الجلسات على الخادم في `SessionTickets` (`SqlSessionTicketStore`).
- Customer endpoints تتطلب Customer ثم resolve store ثم match `store_id` claim.
- Merchant endpoints تتطلب MerchantOwner ثم resolve store ثم match `store_id` claim.
- Platform admin endpoints (`/api/platform/stores/*` وعمليات إدارة MFA بعد التسجيل) تتطلب `PlatformAdmin`.
- Services نفسها تقيد البيانات باستخدام StoreId (+ `CustomerId` في السلة وطلبات العميل).
- تم التحقق من مصفوفة BFLA (Customer/MerchantOwner/PlatformAdmin) وBOLA عبر المتاجر وداخل نفس المتجر والمعرّفات المتداخلة (`zoneId`, `categoryId`, `productId`, `imageId`, `orderId`) وBOPLA (`AuthorizationAndTenantIsolationTests` على SQL Server).

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

## 9. قيود ونطاق غير مثبت

- التوثيق الثنائي الحالي يعتمد على RFC 6238 TOTP وRecovery Codes أحادية الاستخدام؛ **TOTP ليس مقاوماً للتصيد (Phishing-resistant)** لعدم توفر WebAuthn/FIDO2 Passkeys حالياً (راجع NIST SP 800-63B-4 عند اعتماد مستوى AAL2 الكامل).
- لم يظهر في Product Host: Passkeys/FIDO2، واجهة عرض/إدارة الأجهزة النشطة للمستخدم، payment gateway security (COD فقط)، أو CSP/Permissions-Policy/CORS صريحة لـ SPA منفصل.
