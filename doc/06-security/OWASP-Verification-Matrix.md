# مصفوفة تحقق أمني مستندة إلى OWASP

> هذه ليست شهادة OWASP ASVS. المصفوفة تربط الممارسات بالملاحظات الموجودة، وتوضح ما يحتاج إلى اختبار/دليل. لا تُعلن أي مستوى ASVS قبل اختيار المستوى رسمياً وتشغيل verification مناسب.

| مجال التحقق | الدليل الحالي | الحالة التوثيقية | العمل المطلوب |
|---|---|---|---|
| Authentication | Cookie auth, PBKDF2-HMAC-SHA512 (`RetailFlowPasswordHasher`), `PlatformAdmin` mandatory TOTP MFA (`RequireAdminMfa=true`), replay protection, lockout (`5`/`15m`), single-use recovery codes | Verified (Code + SQL Tests; TOTP only) | تقييم إضافة WebAuthn/Passkeys لمقاومة Phishing (NIST AAL2) وتفعيل SMTP الإنتاجي |
| Session management | `__Host-` secure HttpOnly Lax cookie + `SqlSessionTicketStore` (idle/absolute timeout) + per-session `amr=mfa` + `ValidationInterval=Zero` + server-side ticket revocation | Verified (SQL Tests) | إضافة background cleanup للـ expired `SessionTickets` ومراقبة الأداء |
| Authorization | `PlatformAdmin` (`mfa_enrolled=true`, `amr=mfa`) vs `PlatformAdminEnrollment`, role policies, tenant `store_id` claim match filters, BFLA matrix | Verified (`AuthorizationAndTenantIsolationTests`, `PlatformAdminMfaHardeningTests`) | مراجعة دورية لأي endpoints جديدة |
| BOLA / API1 & BOPLA / API3 | tenant-scoped & customer-scoped queries, positional command records, SQL Server tests for nested IDs & over-posting | Verified (`AuthorizationAndTenantIsolationTests`, `OwaspApiSecurityIntegrationTests`) | توسيع التغطية عند إضافة أي endpoints أو DTOs جديدة |
| CSRF | `X-CSRF: 1` on authenticated unsafe methods | Partial / Review Required | استخدم token/Origin defense مضبوطاً، اختبر login and cross-site requests |
| API resource consumption / API4 | global fixed-window rate limiter | Partial | سياسات أكثر دقة للـ auth/checkout/search وbody/size limits |
| Security misconfiguration / API8 | HSTS, HTTPS redirect, host filtering, security headers | Partial | إعداد production `AllowedHosts`, proxy trust, CORS, CSP as applicable |
| Error handling | ProblemDetails and exception handler in Production | Partial | تحقق من كل exceptions وعدم تسرب بيانات داخلية |
| Data protection | Cookie settings and server-side ticket persistence | Not fully evidenced | key ring persistence/sharing, DB TDE/backups, PII inventory |
| Input validation | Catalog validation + Domain invariants | Partial | DTO validation contract لكل endpoint وmax request body |
| Logging/audit | Default logging configuration only visible | Not evidenced for security audit | structured audit for admin/merchant/order/security actions |
| Inventory/Checkout concurrency | rowversion + unique idempotency key | Partial | concurrency stress tests, complete fingerprint, reservation worker |
| API inventory/versioning | dynamic OpenAPI in Development | Partial | API versioning/deprecation and published spec |

## OWASP references

- **ASVS 5.0.0**: إطار متطلبات تحقق أمني لتطبيقات الويب والخدمات، منشور 30 May 2025.
- **OWASP API Security Top 10 2023**: أهم ما يرتبط بهذا النظام API1 BOLA وAPI2 Broken Authentication وAPI3 property authorization وAPI4 resource consumption وAPI5 function-level authorization وAPI6 sensitive business flows وAPI8 misconfiguration وAPI9 inventory.
- **Password Storage Cheat Sheet** و**Session Management Cheat Sheet** و**CSRF Prevention Cheat Sheet**.

## Gate before production security sign-off

1. تحديد ASVS target level ونطاق المكونات.
2. threat model للمتجر/المنصة/session/checkout.
3. مراجعة code لكل endpoint وليس عينة فقط.
4. تشغيل SAST, dependency scan, secret scan, DAST/API tests.
5. اختبار cross-tenant BOLA and function-level authorization.
6. اختبار session fixation/logout/revocation/CSRF على browser.
7. اختبار concurrency وrate limit تحت الضغط.
8. معالجة findings ثم حفظ أدلة قابلة لإعادة التشغيل.
