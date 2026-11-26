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

**CODE:** `src/RetailFlow.Domain/Identity/AppRole.cs`, `src/RetailFlow.Infrastructure/Identity/RetailFlowIdentityUser.cs`, `src/RetailFlow.Api/Hosting/ProductHostExtensions.cs`, `AuthEndpoints.cs`, `MerchantEndpoints.cs`, `PlatformAdminEndpoints.cs`.

## 3. الحدود الحالية والقرارات المعتمدة

- **نطاق MVP التجاري (`ADR-007`):** الهدف بناء MVP تجاري قابل للإنتاج (ليس Demo)، مع قصر الدفع على `COD` في هذه المرحلة دون بناء بوابة دفع إلكتروني، مع الحفاظ على فصل محاور الدفع والطلب للتوسع مستقبلاً.
- **الواجهة المستهدفة (`ADR-007`):** الواجهة المستقبلية `React` ستُستضاف داخل ASP.NET Core Product Host بنمط Same-Origin؛ وحتى الآن لم يظهر كود React/TypeScript في المستودع (يوجد SPA fallback في `Program.cs` لتقديم `wwwroot/index.html` عند توفره).
- **البريد الإلكتروني:** `SmtpEmailSender` و`IRetailFlowEmailSender` منفذان لدعم استعادة كلمة المرور وتأكيد البريد، لكن إعدادات SMTP الإنتاجية لا تزال معلقة قبل التشغيل الحي (`GAP-009`).
- لا يوجد دليل على تكامل شحن خارجي أو SMS أو تقارير مالية أو مخزون متعدد المستودعات.
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
