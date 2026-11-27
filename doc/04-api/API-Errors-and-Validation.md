# الأخطاء والتحقق في API

## رموز HTTP الملحوظة

| الحالة | الاستخدام الموجود |
|---|---|
| 200 | نجاح قراءة أو عملية تُرجع DTO |
| 201 | إنشاء Customer/Store/Product/Order أو موارد أخرى |
| 204 | نجاح update/delete/action بدون body |
| 400 | query validation أو checkout validation أو inventory negative `OnHand` (`validation_error`) |
| 401 | بيانات دخول غير صحيحة أو مستخدم غير مصادق |
| 403 | Authorization أو tenant mismatch أو CSRF marker missing |
| 404 | متجر غير نشط/غير موجود أو مورد لا يخص المتجر |
| 409 | Checkout in progress / concurrency, order state, or inventory conflict (`checkout_concurrent_in_flight`, `checkout_concurrency_conflict`, `cart_concurrency_conflict`, `order_state_conflict`, `order_concurrency_conflict`, `inventory_reservation_conflict`, `inventory_concurrency_conflict`) |
| 412 | فشل شرط `If-Match` على Cart (`cart_concurrency_conflict`) |
| 422 | Idempotency key reused with a different request hash (`checkout_payload_mismatch`) أو فشل قاعدة عمل في Checkout (`checkout_business_rule_failed`) |
| 429 | Rate limit exceeded |
| 500 | خطأ خادم داخلي معقم عبر `RetailFlowExceptionHandler` (`internal_server_error` + `traceId`) |
| 503 | readiness dependency unavailable أو عطل قاعدة بيانات مؤقت (`service_unavailable`) |

## Problem Details

Product Host يسجل `AddProblemDetails`. في Production يمر عبر `UseExceptionHandler()`، ويحوّل RateLimiter وبعض المسارات أخطاءها إلى ProblemDetails. بعض handlers تبني `TypedResults.Problem` أو `ValidationProblem` مباشرة.

تُحوَّل استثناءات تعارض دورة حياة الطلب (`OrderStateConflictException` -> `order_state_conflict` و`OrderConcurrencyConflictException` -> `order_concurrency_conflict`) مركزياً عبر `RetailFlowExceptionHandler` إلى `409 Conflict` بصيغة RFC 9457 ProblemDetails (`type: "about:blank"`, `title: "Conflict"`, `code`, `detail`, `instance`, `traceId`)، بينما تبقى الاستثناءات غير المتوقعة الأخرى `500`/`503`. يُنصح باختبار كل مسار ورصد status codes الفعلية من `WebApplicationFactory`.

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
