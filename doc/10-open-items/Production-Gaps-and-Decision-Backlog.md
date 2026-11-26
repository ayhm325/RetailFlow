# فجوات الإنتاج وقرارات مطلوبة

الأولوية هنا تعكس أثر المخاطر على صحة النظام أو أمانه، وليست نتيجة Penetration Test. ينبغي أن يراجع مالك الهندسة كل بند ويضيف owner/due date/evidence قبل اعتماده.

| ID | Priority | الملاحظة المدعومة | الأثر المحتمل | الإجراء المطلوب |
|---|---|---|---|---|
| GAP-001 | **Closed (Verified)** | Checkout request hash الآن يشمل canonical cart snapshot (`ProductId:Quantity`) ويعيد بناءه من `Order.Items` عند إعادة العرض | `CheckoutService.cs` + `EndToEndCommerceAuthIntegrationTests` (`422` عند تغيّر السلة، `200` عند Replay) | تم التنفيذ والتحقق محلياً (`76/76` pass) |
| GAP-002 | **Closed (Verified)** | `MaintenanceBackgroundService` (`BackgroundService` + `PeriodicTimer`) يستدعي `ReleaseExpiredReservationsAsync` دورياً كل دقيقة | `MaintenanceBackgroundService.cs` + `OrderService.cs` | تم التنفيذ والتحقق محلياً (الموزّع multi-instance يظل بحاجة distributed lock لاحقاً) |
| GAP-003 | **Closed (Verified)** | `CheckoutService` يفحص `ExpiresAtUtc <= now` عند duplicate lookup، و`MaintenanceBackgroundService` يحذف السجلات المنتهية | `CheckoutService.cs` + `MaintenanceBackgroundService.cs` | تم التنفيذ والتحقق محلياً |
| GAP-004 | High | Cookie/session design لا يظهر Data Protection key persistence/sharing ضمن Product Host configuration | جلسات قد تتأثر بإعادة التشغيل أو scale-out | تحديد persistent shared key ring، encryption-at-rest، rotation، deployment validation |
| GAP-005 | **Closed (Verified)** | `MaintenanceBackgroundService` يحذف `SessionTickets` المنتهية (`ExpiresAtUtc <= now`) دورياً | `MaintenanceBackgroundService.cs` | تم التنفيذ والتحقق محلياً |
| GAP-006 | **Closed (Verified)** | CSRF يتحقق من `X-CSRF: 1` **مع** فحص `Origin`/`Referer` (`IsSameOriginIfPresent`) وفق OWASP CSRF Prevention Cheat Sheet | `ProductHostExtensions.cs` + `EndToEndCommerceAuthIntegrationTests` (`https://evil.example` -> `403`) | تم التنفيذ والتحقق محلياً |
| GAP-007 | **Closed (Verified)** | `PasswordHasherService` محدّث إلى **600,000** تكرار (`PBKDF2-HMAC-SHA256`) وفق توصية OWASP Password Storage Cheat Sheet | `PasswordHasherService.cs` + `EndToEndCommerceAuthIntegrationTests` | تم التنفيذ والتحقق محلياً (يحافظ على توافق القراءة للـ hashes القديمة) |
| GAP-008 | **Closed (Verified)** | `RetailFlowCookieEvents.ValidatePrincipal` يتحقق من `AppUser.IsActive` في DB ويستدعي `RejectPrincipal()` + `SignOutAsync()` عند التعطيل وفق Microsoft Learn | `RetailFlowCookieEvents.cs` + `EndToEndCommerceAuthIntegrationTests` (`Deactivate()` -> `401`) | تم التنفيذ والتحقق محلياً |
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
