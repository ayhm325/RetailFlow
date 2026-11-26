# مرجع الإعدادات

## 1. إعدادات قاعدة البيانات

| المفتاح | المعنى | المصدر/السلوك |
|---|---|---|
| `RETAILFLOW_CONNECTION` | connection string الأساسي | الأولوية في Runtime عبر IConfiguration |
| `ConnectionStrings:RetailFlow` | بديل connection string | يستخدم إذا لم يُحدد المفتاح السابق |
| Development fallback | local SQL Server | `Server=.;Database=RetailFlowProduct;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True` |

في Production إذا لم يُحدد أي من المفتاحين يفشل التحقق عند startup عبر options validation.

## 2. Host configuration

| المفتاح | المطلوب |
|---|---|
| `AllowedHosts` | مطلوب صراحة خارج Development؛ wildcard والقيم المفتوحة مرفوضة عبر validator |
| `ASPNETCORE_ENVIRONMENT` | يحدد Development مقابل Production behavior |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | يذكر الكود أنه يمكن تمكين Forwarded Headers في Azure App Service on Linux؛ يجب التحقق من trust boundary قبل استخدامه |

`src/RetailFlow.Api/appsettings.json` الحالي يحتوي Logging فقط ولا يضع AllowedHosts أو connection string.

## 3. Rate limiting

`RateLimiting:PermitLimit` الافتراضي 120، و`RateLimiting:AuthPermitLimit` للمسارات الحساسة (`/auth/login`, `/auth/forgot-password`, `/auth/reset-password`, `/auth/2fa/`) الافتراضي 20، و`RateLimiting:WindowSeconds` الافتراضي 60. يجب أن تكون القيم موجبة؛ الكود يطبق `Math.Max(1, ...)`.

## 4. Email, Platform MFA & Data Protection

| المفتاح | المعنى |
|---|---|
| `Email:PublicBaseUrl` | الأصل الموثوق لبناء روابط استعادة كلمة المرور وتوثيق البريد (يجب أن يكون `https://` فقط في `Production` بدون بيانات اعتماد أو query/fragment) |
| `Email:SmtpHost` / `SmtpPort` / `EnableSsl` / `Username` / `Password` / `FromAddress` / `TimeoutSeconds` | إعدادات SMTP للإرسال الفعلي في الإنتاج؛ يجب أن يكون `EnableSsl = true` في `Production` (`TimeoutSeconds` بين `1` و`120`، الافتراضي `15`) |
| `Email:RequireConfiguredInProduction` | الافتراضي `true`؛ يفشل startup (fail-closed) في `Production` إذا غابت إعدادات SMTP أو `PublicBaseUrl` أو كان `EnableSsl = false` |
| `PlatformSecurity:RequireAdminMfa` | الافتراضي `true`؛ يمنع `PlatformAdmin` غير المسجّل أو غير المتحقق بـ MFA من `/api/platform/stores/*`، ويرفض بدء التشغيل في `Production` إذا عُيّن إلى `false` عبر `PlatformSecurityOptionsValidator` |
| `DataProtection:CertificatePath` / `CertificatePassword` / `CertificateThumbprint` | شهادة X.509 (PKCS#12 مع مفتاح خاص وصلاحية سارية ومطابقة thumbprint اختيارية) لتشفير مفاتيح Data Protection في SQL Server (`DataProtectionSetup.cs`) |
| `DataProtection:PreviousCertificates` | قائمة شهادات سابقة (`CertificatePath` / `CertificatePassword` / `CertificateThumbprint`) لفك تشفير المفاتيح التاريخية أثناء تدوير الشهادة عبر `RetailFlowCertificateResolver` (`ICertificateResolver`) |
| `DataProtection:DefaultKeyLifetimeDays` | عمر المفتاح الافتراضي بالأيام (الافتراضي `90`، مقيد بين `7` و`365`) |
| `DataProtection:RequireCertificateInProduction` | الافتراضي `true`؛ شهادة X.509 إلزامية في `Production` ولا يمكن تعطيلها (`false` يفشل `ValidateOnStart`) |
| سياسة كلمات المرور (`DependencyInjection.cs` / `RetailFlowPasswordValidator.cs`) | الحد الأدنى `15` حرفاً والحد الأقصى `1024` حرفاً، تطبيع Unicode NFC، حظر كلمات المرور الشائعة/الأنماط المتكررة، قفل الحساب بعد `5` محاولات لمدة `15` دقيقة |

## 5. Cookie/session

قيمة 12 ساعة وSlidingExpiration محددة في `ProductHostExtensions.cs` وليست مفتاحاً مقروءاً من config الحالي. أي تحويل لها إلى configuration يجب أن يرافقه validation واختبارات.

## 5. Secrets

- لا تضع connection strings أو password أو Data Protection key material في Git.
- استخدم Secret Manager محلياً وmanaged secret store للبيئات الحية.
- امنع طباعة ticket values وpassword hashes وPII في logs.
- دوّر credentials وفق سياسة موثقة.

## 6. Configuration example (placeholders only)

```json
{
  "ConnectionStrings": {
    "RetailFlow": "<SECRET_STORE_OR_LOCAL_SQL_CONNECTION>"
  },
  "AllowedHosts": "<EXPLICIT_HOSTNAME>"
}
```

المثال توضيحي؛ لا تنسخه حرفياً إلى Production.
