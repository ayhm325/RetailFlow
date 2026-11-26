# الصحة والتشغيل والمراقبة

## Health endpoints

| Endpoint | ما يفحصه | Authentication |
|---|---|---|
| `/health/live` | العملية نفسها؛ predicate يستبعد dependency checks | غير محمي في الكود المعاين |
| `/health/ready` | Health checks الموسومة `ready`؛ وتشمل SQL database check | غير محمي في الكود المعاين |

`DatabaseReadinessHealthCheck` يستخدم `Database.CanConnectAsync`، ويعيد Unhealthy عند فشل الاتصال، ويحافظ على cancellation.

## Logging

`src/RetailFlow.Api/appsettings.json` يحدد Default=Information وMicrosoft.AspNetCore=Warning. لا يظهر ضمن الملفات الأساسية التي تم فحصها إعداد OpenTelemetry أو metrics exporter أو distributed tracing أو audit event pipeline.

## ما يجب قياسه قبل Production

- Request rate, error rate, p50/p95/p99 latency حسب endpoint.
- SQL connection failures, query duration, deadlocks, lock waits.
- Checkout conflict/replay/failure rates.
- Inventory concurrency exceptions.
- Active/expired SessionTickets and storage growth.
- Failed login and rate-limit events.
- 401/403 per route and tenant boundary violations.
- Background reservation release lag (بعد تصميم worker).

## Data operations

- جدولة backups واختبار restores دورياً.
- تحديد RPO/RTO مع مالك المنتج والتشغيل.
- توثيق DB capacity/index maintenance/data retention.
- حماية SQL credentials وbackups وSessionTickets وaddresses.
- وضع retention/cleanup policy لـ CheckoutRequests وSessionTickets وOrderStatusHistories.

## Incident triage

1. تحقق من `/health/live` ثم `/health/ready`.
2. راجع error/trace IDs وSQL availability.
3. افحص آخر deploy والـ migration state.
4. عند checkout incidents، لا تحذف Idempotency rows أو reservations يدوياً قبل حفظ الحالة والتحقق من order/inventory consistency.
5. عند session issue، لا تحذف SessionTickets بالجملة دون تقييم أثر logout على المستخدمين.
