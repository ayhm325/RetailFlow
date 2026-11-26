# نظرة عامة على المنتج

## 1. ما هو RetailFlow حسب التنفيذ الحالي؟

RetailFlow هو Product Host لإدارة متاجر متعددة (multi-store / tenant-scoped commerce) من خلال ASP.NET Core Minimal APIs. يوفر الكود الأساسي وظائف إدارة المتجر والكتالوج والمخزون، تسجيل ودخول المستخدمين، سلة العميل، Checkout، إدارة دورة الطلب، وتجهيز الطلبات بنمط Delivery أو Pickup مع تحصيل نقدي عند الاستلام/التسليم (COD).

هذه الصياغة تصف الموجود في الكود عند baseline، ولا تعني اكتمال كل ما يحتاجه متجر تجاري قابل للإنتاج.

## 2. المستخدمون والأدوار

| الدور | الغرض الظاهر في النظام | Authorization Policy |
|---|---|---|
| Customer | يتصفح متجره، يسجل/يدخل، يدير السلة، ينشئ ويتابع ويلغي طلبه ضمن السياسة | `Customer` |
| MerchantOwner | يدير إعدادات المتجر والكتالوج والمخزون والطلبات | `MerchantOwner` |
| PlatformAdmin | ينشئ المتاجر ويوافق/يرفض/يعلق المتاجر وينشئ حساب مالك متجر | `PlatformAdmin` |

**CODE:** `src/RetailFlow.Domain/Identity/AppUser.cs`, `src/RetailFlow.Api/Hosting/ProductHostExtensions.cs`, `AuthEndpoints.cs`, `MerchantEndpoints.cs`, `PlatformAdminEndpoints.cs`.

## 3. الحدود الحالية

- الواجهة الموجودة في هذا المستودع هي HTTP API؛ لم يظهر React/TypeScript frontend في baseline.
- يوجد SPA fallback في Product Host لتقديم `wwwroot/index.html` عند وجوده، لكن ملفات SPA نفسها لم تظهر في Git tree.
- لا يوجد Provider دفع إلكتروني في Product Host؛ الحالات الموجودة تدعم Pending/Collected/Voided ويظهر مسار COD في endpoint/service.
- لا يوجد دليل على تكامل شحن خارجي أو إشعارات بريد/SMS أو تقارير مالية أو مخزون متعدد المستودعات.
- `IdentitySpike` و`SessionTransportSpike` مشاريع مستقلة للتجارب؛ لا تخلطها مع مسار Product Host.

## 4. خصائص المنتج الملحوظة

1. Store identity والـ slug هما مدخل تحديد المتجر.
2. المتجر له نوع `Physical` أو `OnlineOnly` وحالة Lifecycle.
3. StoreSettings تحتوي العملة والمنطقة الزمنية وخيارات Delivery/Pickup وعناوين عامة وداخلية.
4. المنتجات مرتبطة بمتجر وفئة وسعر وعملة المتجر وحالة نشر وصور ومخزون.
5. العميل يضيف منتجات إلى Cart خاصة به داخل متجر معين.
6. Checkout يحاول منع تكرار العملية بواسطة `Idempotency-Key` وسجل `CheckoutRequests`.
7. الطلب ينسخ بيانات المنتج الأساسية وقت الإنشاء إلى `OrderItem`، ويحتفظ ببيانات حالة الطلب والتجهيز.
8. المخزون يستخدم `OnHand`, `Reserved`, `Available` وSQL `rowversion`.

## 5. ما الذي لا يثبته هذا الوصف؟

لا يثبت ملاءمة المنتج التجارية أو صحة الأسعار والضرائب أو القدرة على تحمل أحمال الإنتاج أو اكتمال إدارة الطلبات/التسويات أو امتثال الأمان. هذه قرارات واختبارات منفصلة موثقة في `10-open-items`.
