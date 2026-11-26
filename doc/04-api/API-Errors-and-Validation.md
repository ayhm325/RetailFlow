# الأخطاء والتحقق في API

## رموز HTTP الملحوظة

| الحالة | الاستخدام الموجود |
|---|---|
| 200 | نجاح قراءة أو عملية تُرجع DTO |
| 201 | إنشاء Customer/Store/Product/Order أو موارد أخرى |
| 204 | نجاح update/delete/action بدون body |
| 400 | query validation أو checkout validation |
| 401 | بيانات دخول غير صحيحة أو مستخدم غير مصادق |
| 403 | Authorization أو tenant mismatch أو CSRF marker missing |
| 404 | متجر غير نشط/غير موجود أو مورد لا يخص المتجر |
| 409 | Checkout in progress / business transition conflict |
| 422 | Idempotency key reused with a different request hash |
| 429 | Rate limit exceeded |
| 503 | readiness dependency unavailable |

## Problem Details

Product Host يسجل `AddProblemDetails`. في Production يمر عبر `UseExceptionHandler()`، ويحوّل RateLimiter وبعض المسارات أخطاءها إلى ProblemDetails. بعض handlers تبني `TypedResults.Problem` أو `ValidationProblem` مباشرة.

لا تفترض أن كل exception في كل Service يتم تحويله إلى نفس response shape؛ أمثلة الـ merchant endpoints لا تحتوي معالجة موحدة محلية لجميع Domain exceptions. يجب اختبار كل مسار ورسم status codes الفعلية من WebApplicationFactory.

## Validation sources

- Catalog query validation: page>=1، pageSize 1..50، search<=100، sort من allowlist.
- Product domain: اسم مطلوب، سعر غير سالب، عملة/دقة مبلغ صحيحة، CategoryId غير فارغ.
- Cart: quantity > 0.
- Checkout: `Idempotency-Key` مطلوب وبحد أقصى 128 حرفاً.
- ProductImage: absolute HTTP/HTTPS URL وحدود طول.
- Store/DeliveryZone: قيود على الحقول والرسوم والعملات ونوع المتجر.

## التوصيات العقدية

1. وثّق لكل endpoint الـ status codes وProblemDetails type وvalidation fields.
2. اعتمد أخطاء ثابتة لا تكشف stack trace أو SQL details.
3. لا تعِد تفاصيل exception الداخلية إلى العميل في Production.
4. أضف contract tests لكل status code موثق.
5. اتفق على شكل موحد لـ `traceId`, `type`, `title`, `status`, `detail`, `errors`.
