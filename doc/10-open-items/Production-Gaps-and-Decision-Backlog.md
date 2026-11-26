# فجوات الإنتاج وقرارات مطلوبة

الأولوية هنا تعكس أثر المخاطر على صحة النظام أو أمانه، وليست نتيجة Penetration Test. ينبغي أن يراجع مالك الهندسة كل بند ويضيف owner/due date/evidence قبل اعتماده.

| ID | Priority | الملاحظة المدعومة | الأثر المحتمل | الإجراء المطلوب |
|---|---|---|---|---|
| GAP-001 | **Closed (Verified)** | Checkout request hash يشمل canonical cart snapshot (`ProductId:Quantity`) ويعيد بناءه من `Order.Items` عند إعادة العرض، مع تراجع ذري كامل عن الحجز الجزئي وإخفاء `ex.Message` | `CheckoutService.cs` + `EndToEndCommerceAuthIntegrationTests` (`Checkout_PartialInventoryFailure_RollsBackAllReservationsAtomically_AndHidesInternalDetails`, `422`, `200`) | تم التنفيذ والتحقق محلياً (`80/80` pass) |
| GAP-002 | **Closed (Verified)** | `MaintenanceBackgroundService` (`PeriodicTimer`) + `OrderService.ReleaseExpiredReservationsAsync` معزول لكل طلب ومتحمّل لتزامن عدة instances (`DbUpdateConcurrencyException`) | `MaintenanceBackgroundService.cs` + `OrderService.cs` + `MaintenanceBackgroundService_ConcurrentMultiInstanceSweep_ReleasesExpiredDataAtMostOnce` | تم التنفيذ والتحقق محلياً بـ 2 instances متزامنة |
| GAP-003 | **Closed (Verified)** | `CheckoutService` يفحص `ExpiresAtUtc <= now` عند duplicate lookup، و`MaintenanceBackgroundService` يحذف السجلات المنتهية | `CheckoutService.cs` + `MaintenanceBackgroundService.cs` + `MaintenanceBackgroundService_ConcurrentMultiInstanceSweep_ReleasesExpiredDataAtMostOnce` | تم التنفيذ والتحقق محلياً |
| GAP-004 | **Closed (DB Persistence Verified)** | `AddDataProtection().SetApplicationName("RetailFlow").PersistKeysToDbContext<RetailFlowDbContext>()` + ترحيل `AddDataProtectionKeys` | `ProductHostExtensions.cs` + `RetailFlowDbContext.cs` + `EndToEndCommerceAuthIntegrationTests` | تم التنفيذ والتحقق محلياً (تشفير المفاتيح في الإنتاج بـ Certificate/KeyVault يفعّل عند النشر) |
| GAP-005 | **Closed (Verified)** | `MaintenanceBackgroundService` يحذف `SessionTickets` المنتهية (`ExpiresAtUtc <= now`) دورياً | `MaintenanceBackgroundService.cs` + `MaintenanceBackgroundService_ConcurrentMultiInstanceSweep_ReleasesExpiredDataAtMostOnce` | تم التنفيذ والتحقق محلياً |
| GAP-006 | **Closed (Verified for API/Fetch)** | CSRF يتحقق من `X-CSRF: 1` + فحص `Sec-Fetch-Site` (`cross-site` مرفوض حتى عند غياب `Origin`/`Referer`) + فحص `Origin`/`Referer` | `ProductHostExtensions.cs` + `EndToEndCommerceAuthIntegrationTests` (`Origin` و`Sec-Fetch-Site: cross-site` -> `403`) | تم التنفيذ والتحقق محلياً (عند بناء React SPA يضاف synchronizer token `IAntiforgery` كطبقة إضافية) |
| GAP-007 | **Closed (Verified)** | `PasswordHasherService` محدّث إلى **600,000** تكرار (`PBKDF2-HMAC-SHA256`) مع الحفاظ على توافق القراءة لـ `310,000` | `PasswordHasherService.cs` + `PasswordHasherTests` (`Hash_Uses600000Iterations_*`, `Verify_SupportsLegacy310000IterationFormat`) | تم التنفيذ والتحقق محلياً (`80/80` pass) |
| GAP-008 | **Closed (Verified)** | `RetailFlowCookieEvents.ValidatePrincipal` يتحقق من `AppUser.IsActive` في DB ويستدعي `RejectPrincipal()` + `SignOutAsync()` عند التعطيل وفق Microsoft Learn | `RetailFlowCookieEvents.cs` + `EndToEndCommerceAuthIntegrationTests` (`Deactivate()` -> `401`) | تم التنفيذ والتحقق محلياً |
| GAP-009 | High | لا تظهر MFA/password reset/email verification/account lockout في Product Host | هجمات credential abuse واستعادة الحساب غير مغطاة | تحديد account lifecycle/security policy |
| GAP-010 | **Closed (Implemented)** | `PlatformBootstrapHostedService` ينشئ أول `PlatformAdmin` لمرة واحدة بشكل idempotent من `PlatformBootstrap:AdminEmail/AdminPassword` دون تخزين كلمات مرور في الكود | `PlatformBootstrapHostedService.cs` + `AuthService.EnsurePlatformAdminAsync` | تم التنفيذ والتحقق من البناء |
| GAP-011 | High | لا توجد deployment manifests/runbook فعلي أو backup/restore procedures في repo | صعوبة نشر موثوق واستعادة بعد فقد البيانات | تحديد host topology وrelease pipeline وRPO/RTO وrestore drills |
| GAP-012 | Medium | لا تظهر CORS policy أو Frontend client في repo | تكامل Browser frontend خارج النطاق غير موثق | تحديد same-origin vs cross-origin hosting ثم CORS/credentials/CSRF policy |
| GAP-013 | Medium | لا تظهر API versioning/deprecation policy | تغييرات العقود قد تكسر clients | اعتماد API versioning and contract governance |
| GAP-014 | **Closed (Verified)** | `GET /orders` للعميل والتاجر يدعمان `page` و`pageSize` (الحد الأقصى 50) ويعيدان `PagedOrdersResponse` | `OrderService.cs` + `CustomerOrderEndpoints.cs` + `MerchantEndpoints.cs` + `EndToEndCommerceAuthIntegrationTests` | تم التنفيذ والتحقق محلياً (`76/76` pass) |
| GAP-015 | **Closed (Audit Logging)** | سجلات تدقيق مهيكلة (`RetailFlow.Audit.Auth`, `RetailFlow.Audit.Platform`) بأحداث التسجيل والدخول وحالات المتجر دون تسجيل كلمات مرور أو PII حساس | `AuthEndpoints.cs` + `PlatformAdminEndpoints.cs` + `PlatformBootstrapHostedService.cs` | تم التنفيذ (مصدّرات OpenTelemetry تضاف عند ربط بيئة الإنتاج) |
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
