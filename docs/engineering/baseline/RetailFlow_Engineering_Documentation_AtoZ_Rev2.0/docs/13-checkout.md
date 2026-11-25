# 13. العملاء والسلة وCheckout والطلبات

## 13.1 Customer Profile

افصل credential identity عن الملف التجاري. Customer Profile والعناوين Store-scoped. حساب العميل في متجر لا يمنحه وصولاً إلى حسابه في متجر آخر.

## 13.2 Cart

Cart تتبع Customer + Store. لا تسمح بمنتجات متجرين. عند تغيير السعر أو الحالة أظهر التغيير وأعد التحقق. وجود item في السلة لا يحجز المخزون تلقائياً ما لم يصدر قرار منتج بذلك.

## 13.3 Checkout contract

مدخل Checkout المقترح يحتوي fulfillment selection/address/zone مع correlation metadata، ولا يقبل totals أو authoritative prices أو delivery fee أو StoreId كحقائق يحددها العميل. استخدم `Idempotency-Key` بعد اعتماد scope/retention/replay semantics.

## 13.4 Pricing

أعد القراءة من DB، واحسب Line Totals وفق Currency Minor Unit، وحدد Flat Fee من DeliveryZone، وامنع negative values، وثبت rounding policy صراحة. الضرائب والخصومات والكوبونات غير محسومة ولا تُضاف ضمناً.

## 13.5 Order snapshots

OrderItem يحتفظ ProductNameSnapshot وUnitPriceSnapshot وQuantity وCurrency حسب التصميم. Order يحتفظ FulfillmentMethod والعنوان/zone/fee snapshots. تغيير السعر أو عنوان العميل لاحقاً لا يعيد تفسير طلب سابق.

## 13.6 Order state model

المسار التصوري: PendingConfirmation -> Confirmed -> Processing -> Packed -> Shipped/OutForDelivery أو ReadyForPickup -> Delivered/PickedUp -> Completed. Cancelled مسموح من حالات محددة حسب actor/policy. هذا النموذج ليس معتمداً حتى إقرار transition table.

## 13.7 Payment Status مستقل

MVP يبدأ COD. PaymentStatus منفصل عن Order/Fulfillment. يلزم قرار من يؤكد التحصيل ومتى، وكيف تسجل محاولة تحصيل فاشلة أو رفض الاستلام. لا تجعل Delivered مرادفاً لـ Collected تلقائياً.

## 13.8 History وCancellation

StatusHistory يسجل Previous/New/Actor/Time/Reason. Cancellation يتحقق من state/actor/policy، ثم transition + release one time ضمن حدود التزامن. لا تلغِ Completed في المسار الاعتيادي.
