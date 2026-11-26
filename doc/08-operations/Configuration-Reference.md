# مرجع الإعدادات

## 1. إعدادات قاعدة البيانات

| المفتاح | المعنى | المصدر/السلوك |
|---|---|---|
| `RETAILFLOW_CONNECTION` | connection string الأساسي | الأولوية في Runtime عبر IConfiguration |
| `ConnectionStrings:RetailFlow` | بديل connection string | يستخدم إذا لم يُحدد المفتاح السابق |
| Development fallback | local SQL Server | `Server=.;Database=RetailFlowProduct;Trusted_Connection=True;Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=True` |

في Production إذا لم يُحدد أي من المفتاحين يفشل التحقق عند startup عبر options validation.

## 2. Host configuration

| المفتاح | المطلوب |
|---|---|
| `AllowedHosts` | مطلوب صراحة خارج Development؛ wildcard والقيم المفتوحة مرفوضة عبر validator |
| `ASPNETCORE_ENVIRONMENT` | يحدد Development مقابل Production behavior |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | يذكر الكود أنه يمكن تمكين Forwarded Headers في Azure App Service on Linux؛ يجب التحقق من trust boundary قبل استخدامه |

`src/RetailFlow.Api/appsettings.json` الحالي يحتوي Logging فقط ولا يضع AllowedHosts أو connection string.

## 3. Rate limiting

`RateLimiting:PermitLimit` الافتراضي 120، و`RateLimiting:WindowSeconds` الافتراضي 60. يجب أن تكون القيم موجبة؛ الكود يطبق `Math.Max(1, ...)`.

## 4. Cookie/session

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
