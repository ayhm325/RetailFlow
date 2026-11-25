# 18. التشغيل والاعتمادية والمراقبة (SRE & Operations)

## 18.1 مبادئ التشغيل

- افصل Liveness عن Readiness؛ Liveness يثبت أن العملية حية، Readiness يثبت توفر الاعتماديات المطلوبة لتلقي العمل.
- Health checks لا تعرض أسراراً أو استثناءات داخلية للعميل العام.
- أضف Structured Logging وDistributed Tracing وMetrics مع correlation/trace IDs.
- حدد runbooks للحوادث المتكررة: SQL unavailable، deployment failure، failed migration، storage outage، queue backlog إذا أضيفت queues.

## 18.2 SLI/SLO المقترحة للاعتماد

الأرقام التالية ليست التزامات أو قياسات حالية؛ يجب على Product Owner تحديدها بعد معرفة الحمل والكلفة والميزانية.

| SLI | طريقة القياس | SLO المراد اعتماده |
|---|---|---|
| API availability | successful eligible requests / total | TBD |
| API latency | P50/P95/P99 حسب endpoint | TBD |
| Catalog freshness | وقت ظهور تعديل المنتج للعميل | TBD |
| Checkout success | completed valid attempts / valid attempts | TBD |
| Order integrity | order/inventory invariant violations | هدف صفر |
| Recovery time | زمن الاستعادة بعد incident | RTO TBD |
| Data loss window | آخر نقطة بيانات قابلة للاستعادة | RPO TBD |

لا تضع SLO قبل تعريف eligible request، نوافذ القياس، الاستثناءات، مصدر telemetry، وحجم العينة.

## 18.3 Observability

### Logs

سجلات منظمة تشمل Timestamp UTC وSeverity وService/Environment وTraceId وOperation وStoreId الداخلي عند الضرورة وبما لا يكشف PII. لا تسجل بيانات البطاقة أو كلمات المرور أو cookies/tickets.

### Metrics

مقاييس تقنية: request rate/errors/duration، SQL duration/pool/timeout، CPU/memory، GC، queue depth إن أضيفت، active sessions إن أمكن دون تحديد هوية المستخدم.

مقاييس تجارية: orders created/cancelled، checkout failures by safe reason code، inventory reservation conflicts، fulfillment backlog. لا تستخدم labels عالية cardinality مثل customer email أو order id.

### Traces

اربط request بـ Application Use Case ثم SQL spans ومزود الرسائل. اضبط sampling وredaction، ولا تسجل query parameters التي قد تحتوي PII.

## 18.4 Backup & Restore

- حدد RPO/RTO مع مالك العمل؛ لا تضع أرقاماً غير ممولة أو غير مختبرة.
- نسخ احتياطية تلقائية وفق قدرات منصة DB، مع retention متفق عليه وتشفير.
- احتفظ بنسخة منفصلة/محميّة من Data Protection keys والشهادات أو آلية استعادتها، دون تخزينها بجانب backup غير محمي.
- اختبر Point-in-Time Restore إلى بيئة معزولة، ثم تحقق من migrations، سلامة FK، عدد السجلات ومؤشرات المجال.
- نفذ restore drill دورياً، وسجل الزمن والنتيجة والفجوات.
- اختبر سيناريو فقدان مفاتيح Data Protection؛ استعادة قاعدة البيانات وحدها قد لا تعيد صلاحية فك التذاكر/البيانات المحمية.

## 18.5 Incident Management

1. اكتشاف وتنبيه عبر SLI/SLO أو بلاغ.
2. تصنيف شدة الحادث وتأثيره على المتاجر والبيانات.
3. إيقاف الضرر/عزل المسار المتأثر مع الحفاظ على الأدلة.
4. استعادة الخدمة عبر rollback أو forward fix أو failover.
5. التحقق من سلامة orders/inventory/identity بعد الاستعادة.
6. Post-Incident Review بلا لوم، يتضمن timeline وroot cause وcontributing factors وcorrective actions وowner/date.
