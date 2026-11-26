# فجوات الإنتاج وقرارات مطلوبة

الأولوية هنا تعكس أثر المخاطر على صحة النظام أو أمانه، وليست نتيجة Penetration Test. ينبغي أن يراجع مالك الهندسة كل بند ويضيف owner/due date/evidence قبل اعتماده.

| ID | Priority | الملاحظة المدعومة | الأثر المحتمل | الإجراء المطلوب |
|---|---|---|---|---|
| GAP-001 | Critical to validate | Checkout request hash لا يتضمن cart items/quantities | Idempotency قد لا يميّز checkout payload الكامل إذا تغيرت السلة | تعريف canonical cart snapshot/fingerprint واختبار same-key/different-cart |
| GAP-002 | High | `ReleaseExpiredReservationsAsync` موجودة لكن لا يظهر scheduler/hosted worker في Product Host | قد تبقى reservations المنتهية محجوزة ولا يعود المخزون متاحاً | بناء worker/queue أو job schedule مع distributed-safe processing واختبار متعدد instances |
| GAP-003 | High | `CheckoutRequest.ExpiresAtUtc` يُحفظ لكن الخدمة لا تفحصه عند duplicate lookup ولا تنظّف السجلات | مفاتيح idempotency قد تبقى محجوزة بعد التاريخ المعلن | تحديد retention/reuse semantics وتنفيذ cleanup/expiry policy |
| GAP-004 | High | Cookie/session design لا يظهر Data Protection key persistence/sharing ضمن Product Host configuration | جلسات قد تتأثر بإعادة التشغيل أو scale-out | تحديد persistent shared key ring، encryption-at-rest، rotation، deployment validation |
| GAP-005 | High | لا يظهر cleanup worker لـ SessionTickets في Product Host | تراكم سجلات منتهية في DB | job cleanup مع batch/index monitoring وtests |
| GAP-006 | High | CSRF control يتحقق من `X-CSRF: 1` الثابت دون Origin/Referer validation داخل Product Host middleware | يحتاج threat review خاصة مع cookie auth وbrowser topology | اعتماد نمط CSRF موثق (framework antiforgery/synchronizer token أو دفاع layered موثوق) واختبارات browser |
| GAP-007 | High | PBKDF2-HMAC-SHA256 iterations=310,000 بينما OWASP الحالي يذكر 600,000 على الأقل في سياق FIPS-140 | تعليق OWASP compliant قد يكون أوسع من الدليل الحالي | مراجعة baseline مقابل متطلبات FIPS/OWASP وCPU budget، versioned hash upgrade |
| GAP-008 | High | لا يظهر revalidation لحالة AppUser/IsActive في كل طلب بعد إصدار cookie | تعطيل الحساب قد لا يبطل tickets الموجودة تلقائياً | تصميم security stamp/session revocation أو user status revalidation |
| GAP-009 | High | لا تظهر MFA/password reset/email verification/account lockout في Product Host | هجمات credential abuse واستعادة الحساب غير مغطاة | تحديد account lifecycle/security policy |
| GAP-010 | High | لا يوجد bootstrap موثق/آمن لأول PlatformAdmin في الحل الأساسي | قد يتعذر إعداد المنصة أو يتم bootstrap بطريقة يدوية غير محكومة | إنشاء one-time provisioning process auditable ومقيد |
| GAP-011 | High | لا توجد deployment manifests/runbook فعلي أو backup/restore procedures في repo | صعوبة نشر موثوق واستعادة بعد فقد البيانات | تحديد host topology وrelease pipeline وRPO/RTO وrestore drills |
| GAP-012 | Medium | لا تظهر CORS policy أو Frontend client في repo | تكامل Browser frontend خارج النطاق غير موثق | تحديد same-origin vs cross-origin hosting ثم CORS/credentials/CSRF policy |
| GAP-013 | Medium | لا تظهر API versioning/deprecation policy | تغييرات العقود قد تكسر clients | اعتماد API versioning and contract governance |
| GAP-014 | Medium | order/cart list routes لا توضح pagination/limits في العقود المعاينة | قوائم كبيرة قد ترفع resource usage | تحديد paging/sorting/filtering limits |
| GAP-015 | Medium | لا تظهر observability exporters أو security audit pipeline | صعوبة التحقيق والمراقبة | structured logs, metrics, traces, audit events مع PII redaction |
| GAP-016 | Medium | لا توجد بوابة دفع/Refund مثبتة | الدفع الحالي COD فقط | حسم نطاق الدفع والامتثال والتكامل الخارجي |
| GAP-017 | Medium | عنوان StoreSettings.FulfillmentAddress حساس وموجود في DB | تسرب بيانات تشغيلية لو ظهر في DTO غير مقصود | data classification + response mapping tests لكل endpoint |
| GAP-018 | Medium | no evidence of load/performance/restore testing | الأداء والتعافي غير مثبتين | load, concurrency, restore and failover tests |

## قرارات مالك المنتج المطلوبة

1. هل الدفع COD فقط في أول إصدار؟ وهل Delivery/Pickup شروط إلزامية حسب StoreType وStoreSettings؟
2. ما قواعد مدة الحجز، انتهاء الطلب، الإلغاء من العميل/التاجر، وتحرير المخزون؟
3. ما الـ session model المطلوب: idle timeout، absolute timeout، تعدد الأجهزة، revoke all، password-change invalidation؟
4. ما مستوى ASVS المستهدف ومن يملك sign-off الأمني؟
5. ما سياسات حفظ بيانات الطلب والعناوين والجلسات وسجلات التدقيق؟
6. ما بيئة Production ومَن يملك DB/backup/release operations؟
7. هل واجهة React مشروع منفصل أو ستُخدم static assets من Product Host؟

## قاعدة الإغلاق

لا يُغلق أي GAP بمجرد إضافة نص في وثيقة. يتطلب إما تغييراً في الكود مع اختبار، أو قراراً موقعاً يبرر عدم المعالجة، مع evidence ورابط commit أو test run.
