# فجوات الإنتاج وقرارات مطلوبة

الأولوية هنا تعكس أثر المخاطر على صحة النظام أو أمانه، وليست نتيجة Penetration Test. ينبغي أن يراجع مالك الهندسة كل بند ويضيف owner/due date/evidence قبل اعتماده.

| ID | Priority | الملاحظة المدعومة | الأثر المحتمل | الإجراء المطلوب |
|---|---|---|---|---|
| GAP-001 | **Verified — Tests Passed** | Checkout request hash يشمل canonical cart snapshot (`ProductId:Quantity`) ويعيد بناءه من `Order.Items` عند Replay، مع تراجع ذري كامل عن الحجز الجزئي وإخفاء `ex.Message` | `CheckoutService.cs` + `EndToEndCommerceAuthIntegrationTests` + `AuditPhasesIntegrationTests.Phase2And3_*` | تم التحقق محلياً (`11/11` unit + `75/75` integration = `86/86` pass على `5241c3a` + التعديلات المحلية) |
| GAP-002 | **Verified — Tests Passed** | `MaintenanceBackgroundService` (`PeriodicTimer` + `TimeProvider`) بعزل Scope لكل مهمة و`OrderService.ReleaseExpiredReservationsAsync` بوحدة عمل مستقلة ومعاملة صريحة لكل طلب (`IServiceScopeFactory` + bounded retry عند `DbUpdateConcurrencyException`) بدون `ChangeTracker.Clear()` على دفعة مشتركة | `MaintenanceBackgroundService.cs` + `OrderService.cs` + `MaintenanceBatchConcurrencyTests` + `MaintenanceBackgroundService_ConcurrentMultiInstanceSweep_ReleasesExpiredDataAtMostOnce` | تم التحقق محلياً على SQL Server (سيناريوهات A–F + 2 instances متزامنة) |
| GAP-003 | **Verified — Tests Passed** | فصل Processing Lease (`LeaseExpiresAtUtc=60s`, `ClaimToken`) عن Retention (`ExpiresAtUtc=24h`) + Read-After-Error + استرداد ذري للمطالبات المنتهية/الفاشلة | `CheckoutRequest.cs` + `CheckoutService.cs` + `AuditPhasesIntegrationTests.Phase2And3_*` | تم التحقق محلياً (3-step expired lifecycle + stale lease reclaim) |
| GAP-004 | **Production Configuration Pending** | `PersistKeysToDbContext<RetailFlowDbContext>()` + دعم `DataProtection:CertificatePath` (`ProtectKeysWithCertificate`)؛ تشفير المفاتيح ومشاركتها بين Providers مختبَرة بشهادة اختبارية | `ProductHostExtensions.cs` + `RetailFlowDbContext.cs` + `Phase6_DataProtection_EncryptedKeysWithCertificate_ShareAcrossProviders` | DB Persistence & Encryption-at-Rest مختبران؛ يتطلب تزويد شهادة الإنتاج أو Azure Key Vault + Managed Identity عند النشر |
| GAP-005 | **Verified — Tests Passed** | `MaintenanceBackgroundService` يحذف `SessionTickets` المنتهية في Scope مستقل دون التأثر بفشل المهام المجاورة | `MaintenanceBackgroundService.cs` + `MaintenanceBackgroundService_ConcurrentMultiInstanceSweep_ReleasesExpiredDataAtMostOnce` | تم التحقق محلياً |
| GAP-006 | **Verified — Tests Passed** *(API/Fetch)* | CSRF يتحقق من `X-CSRF: 1` + فحص `Sec-Fetch-Site` (`cross-site` مرفوض حتى عند غياب `Origin`/`Referer`) + فحص `Origin`/`Referer` | `ProductHostExtensions.cs` + `EndToEndCommerceAuthIntegrationTests` | تم التحقق محلياً؛ عند بناء React SPA يضاف `IAntiforgery` synchronizer token |
| GAP-007 | **Verified — Tests Passed** | تم اعتماد `RetailFlowPasswordHasher : IPasswordHasher<RetailFlowIdentityUser>` (Identity V3 PBKDF2-HMAC-SHA512 `220,000` تكرار) مع توافق التجزئات القديمة `v1.{Iterations}.{Salt}.{Hash}` (`310k..1M`) وترقية ذرية تلقائية عند تسجيل الدخول | `RetailFlowPasswordHasher.cs` + `AuthService.cs` + `PasswordHasherTests` + `Phase1_LegacyHashMigration_*` | تم التحقق محلياً (`StoreUser` + `PlatformAdmin` + concurrent login) |
| GAP-008 | **Verified — Tests Passed** | `RetailFlowCookieEvents.ValidatePrincipal` يدمج `SecurityStampValidator.ValidatePrincipalAsync` مع فحص `RetailFlowIdentityUser.IsActive` و`store_id` عبر `ScopedUserStore` | `RetailFlowCookieEvents.cs` + `EndToEndCommerceAuthIntegrationTests` + `IdentityAdoptionIntegrationTests` | تم التحقق محلياً (`DeactivateUserAsync` + `PasswordReset` -> `401`) |
| GAP-009 | **Partially Implemented — Production Config & Policy Pending** | تم تحصين دورة الهوية (`IdentitySecurityHardeningTests`): آلة حالات تسجيل TOTP، تشفير أسرار TOTP عبر Data Protection (`dp:v1:`)، Step-up Auth لـ Setup/Replace/Disable/Recovery، حد محاولات MFA + منع إعادة استخدام TOTP (`LastUsedTotpTimeStep`)، منع إعادة استخدام Recovery Codes بالتوازي، سياسة `PlatformSecurity:RequireAdminMfa`، و`SmtpEmailSender` لـ Password Reset/Email Confirmation؛ المتبقي تزويد إعدادات SMTP الإنتاجية واعتماد سياسة إلزام MFA لـ `PlatformAdmin` و`RequireConfirmedEmail` | `AuthService.cs`, `AuthEndpoints.cs`, `ScopedUserStore.cs`, `TotpCryptoHelper.cs`, `EmailDelivery.cs`, `IdentitySecurityHardeningTests` | البنية والضوابط مختبرة محلياً على SQL Server؛ يتطلب تزويد SMTP وإقرار السياسة الإنتاجية |
| GAP-010 | **Verified — Tests Passed** | `PlatformBootstrapHostedService` + `AuthService.EnsurePlatformAdminAsync` مع `sp_getapplock` + معالجة unique constraint `2601/2627` + fail-fast في Production عند `RequireAdminInProduction=true` | `PlatformBootstrapHostedService.cs` + `AuthService.cs` + `Phase5_ConcurrentPlatformAdminBootstrap_CreatesAtMostOneAdmin` | تم التحقق محلياً بـ 2 instances متزامنة |
| GAP-011 | **Open** | لا توجد ملفات نشر Azure، خطط Backup/Restore/Drills، أو RPO/RTO معتمدة في repo | `.github/workflows/`, `doc/08-operations/Deployment-Runbook.md` | مطلوب قبل الإطلاق الإنتاجي |
| GAP-012 | **Deferred — Documented Decision** | التصميم يعتمد Same-Origin (`Product Host/wwwroot`) بدون CORS واسع (`AllowAnyOrigin`/`AllowCredentials` مرفوضان) | `ProductHostExtensions.cs` | يُراجع فقط إذا فُصل React SPA على origin مستقل |
| GAP-013 | **Open** | لا توجد سياسة API Versioning / Deprecation بعد | `API-Reference.md` | يُعتمد قبل تثبيت عقود React Frontend |
| GAP-014 | **Verified — Tests Passed** | `OrderService` يدعم ترقيماً مقيّداً (`MaxPageSize=50`)، وحماية `long` ضد overflow، وترتيباً ثانوياً بـ `Id` (`SqlGuid`) | `OrderService.cs` + `CustomerOrderEndpoints.cs` + `MerchantEndpoints.cs` + `Phase7_OrderPagination_*` | تم التحقق محلياً |
| GAP-015 | **Open** *(Audit Logs Implemented — Telemetry Pending)* | سجلات التدقيق المهيكلة (`RetailFlow.Audit.Auth`, `RetailFlow.Audit.Platform`) منفذة؛ OpenTelemetry Metrics/Tracing/OTLP Exporters غير منفذة بعد | `AuthEndpoints.cs` + `PlatformAdminEndpoints.cs` | إضافة OpenTelemetry instrumentation ومصدّرات الإنتاج |
| GAP-016 | **Deferred — Documented Decision** | الدفع COD فقط في MVP (لا بوابة دفع إلكتروني ولا Refund) | `Order.cs`, `ADR-0005` | قرار منتج معتمد للـ MVP |
| GAP-017 | **Verified — Tests Passed** | `StoreSettings.FulfillmentAddress` مستبعد من `PublicStoreIdentityDto` ومختبر | `PublicCatalogQueryService.cs` + `PublicStoreCatalogIntegrationTests` | تم التحقق محلياً |
| GAP-018 | **Open** | لا توجد اختبارات حمل/أداء (k6/NBomber) لقياس P95/P99 وCookie DB lookup وCheckout concurrency | `doc/07-quality/Testing-Strategy.md` | مطلوب قبل الإطلاق الإنتاجي |

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
