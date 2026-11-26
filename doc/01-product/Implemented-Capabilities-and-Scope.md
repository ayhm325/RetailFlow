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
| OBS-011 | Customer registration/login وPlatform admin login | Implemented | `AuthEndpoints`, `AuthService` |
| OBS-012 | Cookie authentication مع SQL-backed ticket store | Implemented | `ProductHostExtensions`, `SqlSessionTicketStore` |
| OBS-013 | Cart per store/customer | Implemented | `Cart`, `CartService`, customer endpoints |
| OBS-014 | Checkout مع Idempotency-Key وسجل CheckoutRequest | Implemented جزئياً من ناحية الضمانات؛ راجع open issues | `CheckoutService`, `CheckoutRequest` |
| OBS-015 | حجز مخزون للطلب وتحريره/تثبيته حسب الحالة | Implemented؛ التشغيل الدوري غير مثبت | `InventoryItem`, `CheckoutService`, `OrderService` |
| OBS-016 | Customer order history/details/cancel | Implemented | `CustomerOrderEndpoints`, `OrderService` |
| OBS-017 | Merchant order confirm/reject/fulfillment/COD collect | Implemented | `MerchantEndpoints`, `OrderService` |
| OBS-018 | Health live/ready وOpenAPI في Development | Implemented | `ProductHostExtensions` |
| OBS-019 | Rate limiting وSecurity response headers وCSRF request marker | Implemented with verification gaps | `ProductHostExtensions` |

## خارج النطاق المثبت

لم يظهر في Product Host عند baseline: بوابة دفع إلكتروني، استرجاع/Refund، رسوم/ضرائب، كوبونات، شحن مع شركات خارجية، إدارة إرجاع، تعدد مستودعات، تقارير محاسبية، إشعارات، بوابة Frontend React، إدارة صلاحيات دقيقة تتجاوز الأدوار الثلاثة، MFA، استعادة كلمة المرور، توثيق البريد الإلكتروني، أو إدارة دورة حساب كاملة.

وجود مفاهيم في Roadmap أو في `experiments/` لا يغير حالة هذه القائمة. يجب إضافة كل قدرة جديدة إلى هذا الجدول بعد ربطها بـ endpoint وخدمة واختبار.
