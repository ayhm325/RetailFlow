# النطاق والقدرات المنفذة

> المعرفات التالية `OBS-*` معرفات توثيقية جديدة لوصف ما لوحظ في التنفيذ. ليست متطلبات أصلية معتمدة من مالك المنتج.

| ID | القدرة الملحوظة | الحالة | دليل التنفيذ الأساسي |
|---|---|---|---|
| OBS-001 | إنشاء متجر بحالة PendingApproval مع StoreSettings | Implemented | `PlatformStoreService.CreateStoreAsync` |
| OBS-002 | اعتماد/رفض/تعليق متجر مع قيود انتقال | Implemented | `Store.cs`, `PlatformStoreService` |
| OBS-003 | حل متجر Active من route slug لكل طلب متجر | Implemented | `StoreResolutionEndpointFilter`, `StoreDirectoryResolver` |
| OBS-004 | عرض هوية المتجر العامة ومعلومات Branding | Implemented | `PublicCatalogEndpoints`, `PublicCatalogQueryService` |
| OBS-005 | عرض delivery zones المفعّلة فقط | Implemented | `GetEnabledDeliveryZonesAsync` |
| OBS-006 | عرض فئات فعّالة ومنتجات Published فقط | Implemented | `PublicCatalogQueryService` |
| OBS-007 | البحث والفرز والترقيم للمنتجات | Implemented | `CatalogProductsQuery`, `PublicCatalogEndpoints` |
| OBS-008 | قراءة تفاصيل منتج وصوره | Implemented | `GetProductDetailsAsync` |
| OBS-009 | تعديل Branding والعناوين وخيارات التوصيل ومناطق التوصيل | Implemented | `MerchantEndpoints`, `MerchantStoreService` |
| OBS-010 | CRUD محدود للفئات والمنتجات والصور والمخزون | Implemented | `MerchantEndpoints`, `MerchantCatalogService` |
| OBS-011 | مصادقة ASP.NET Core Identity (`Store` و`PlatformAdmin` و`PlatformAdmin` MFA/TOTP واستعادة كلمة المرور وتأكيد البريد) | Implemented | `AuthEndpoints`, `AuthService`, `RetailFlowIdentityUser` |
| OBS-012 | Cookie authentication مع SQL-backed ticket store (idle/absolute timeout) وData Protection بشهادة X.509 | Implemented | `ProductHostExtensions`, `SqlSessionTicketStore`, `DataProtectionSetup` |
| OBS-013 | Cart per store/customer مع ETag/`If-Match` و`RowVersion` | Implemented | `Cart`, `CartService`, customer endpoints |
| OBS-014 | Checkout مع Idempotency-Key، cart snapshot hash، processing lease، واسترداد الأعطال | Implemented | `CheckoutService`, `CheckoutRequest` |
| OBS-015 | حجز مخزون للطلب وتحريره/تثبيته حسب الحالة + تحرير الحجوزات المنتهية دورياً | Implemented | `InventoryItem`, `CheckoutService`, `OrderService`, `MaintenanceBackgroundService` |
| OBS-016 | Customer order history/details/cancel | Implemented | `CustomerOrderEndpoints`, `OrderService` |
| OBS-017 | Merchant order confirm/reject/fulfillment/COD collect | Implemented | `MerchantEndpoints`, `OrderService` |
| OBS-018 | Health live/ready وOpenAPI في Development | Implemented | `ProductHostExtensions` |
| OBS-019 | Rate limiting وSecurity response headers وCSRF (`X-CSRF: 1` + `Sec-Fetch-Site` + `Origin`) | Implemented | `ProductHostExtensions` |

## خارج النطاق المثبت (Post-MVP / Future)

لم يظهر في Product Host عند baseline الحالي: بوابة دفع إلكتروني، استرجاع/Refund، رسوم/ضرائب، كوبونات، شحن مع شركات خارجية، إدارة إرجاع، تعدد مستودعات، تقارير محاسبية، واجهة React SPA فعلية (مستهدفة في Same-Origin عبر `ADR-007`)، أو Passkeys/FIDO2.

وجود مفاهيم في Roadmap أو في `experiments/` لا يغير حالة هذه القائمة. يجب إضافة كل قدرة جديدة إلى هذا الجدول بعد ربطها بـ endpoint وخدمة واختبار.
