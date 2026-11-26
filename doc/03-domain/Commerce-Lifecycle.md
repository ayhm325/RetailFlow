# دورة التجارة والطلبات

## 1. إضافة عناصر إلى السلة

- العميل مصادق عليه بسياسة Customer.
- المسار `/api/stores/{slug}` يحل المتجر، ثم يطابق `store_id` claim مع StoreId الحالي.
- `CartService.UpsertItemAsync` يقبل المنتج إذا كان في المتجر نفسه، Published، وفئته موجودة وفعّالة.
- الكمية يجب أن تكون أكبر من صفر.
- Cart فريدة لكل StoreId/CustomerId، وCartItem فريدة لكل StoreId/CartId/ProductId.
- لا يتم حجز المخزون عند تعديل Cart؛ يظهر `IsAvailable` عند قراءة السلة وفق Available الحالي.

## 2. Checkout

### الإدخال

`POST /api/stores/{slug}/checkout` مع Cookie مصادقة وHeader `Idempotency-Key` وbody يحتوي FulfillmentMode وDeliveryZoneId? وDeliveryAddress?.

### الخطوات الملاحظة

1. قراءة Cart الحالية وبناء `cartSignature` (`ProductId:Quantity` مرتبة).
2. إنشاء SHA-256 request hash من store/customer/fulfillment mode/delivery zone/address و`cartSignature`.
3. إدراج CheckoutRequest بحالة InProgress (مع `ClaimToken` و`LeaseExpiresAtUtc=60s` و`ExpiresAtUtc=24h`) وحفظه.
4. عند unique conflict: إذا انتهى `ExpiresAtUtc` يُحذف السجل المتقادم ويُعاد الاستحواذ؛ وإلا تحميل السجل الموجود ومقارنة hash (يعيد بناء `cartSignature` من `Order.Items` عند Replay)، ثم إرجاع PayloadMismatch أو ConcurrentInFlight (إذا كان Lease سارياً) أو استرداد ذري عند انتهاء Lease/فشل سابق أو Replayed عند وجود Order مكتمل.
5. التحقق من أن المتجر Active وله Currency.
6. في Delivery، يشترط وجود DeliveryZoneId صالح ومفعّل ضمن المتجر نفسه.
7. جلب المنتجات مع Inventory وCategory والتحقق أنها Published وفئتها فعالة ولها Inventory.
8. حجز كمية كل منتج عبر InventoryItem.Reserve.
9. إنشاء Order وOrderItems، وتخزين delivery fee/zone name/address حيث يلزم.
10. إزالة CartItems وتغيير CheckoutRequest إلى Completed وحفظ التغييرات.

### نقاط مهمة

- يتم التحقق من المخزون عند Checkout، لا عند إضافة Cart.
- التحقق من DeliveryEnabled على StoreSettings يجب أن يُراجع مقابل كل مسار checkout؛ لا تستنتج أن وجود delivery zone وحده يغطي كل سياسة تفعيل.
- يتضمن request hash محتوى Cart والكميات (`cartSignature`)، ويُعاد بناؤه من `Order.Items` عند Replay.
- الـ claim الأول يُحفظ قبل معاملة `ReadCommitted` التي تحفظ Order والمخزون وإفراغ السلة واكتمال الـ claim مع التحقق من `ClaimToken` و`LeaseExpiresAtUtc`.
- يوجد `MaintenanceBackgroundService` مسجل في `ProductHostExtensions` يستدعي `ReleaseExpiredReservationsAsync` دورياً كل دقيقة عبر `PeriodicTimer` (مختبر في `MaintenanceBatchConcurrencyTests`).

## 3. انتقالات الطلب

### OrderStatus

| من | إلى | الفاعل/الشرط |
|---|---|---|
| Pending | Confirmed | MerchantOwner |
| Pending | Rejected | MerchantOwner مع سبب |
| Pending | Cancelled | Customer المالك أو نظام انتهاء الحجز |
| Pending / Confirmed | Cancelled | Merchant/System بحسب شروط `CancelByMerchantOrTimeout` |
| Confirmed | Completed | بعد Delivered أو PickedUp ثم تحصيل COD |

### FulfillmentStatus

| النمط | الانتقال |
|---|---|
| عام | Unfulfilled → Preparing |
| Delivery | Preparing → OutForDelivery → Delivered أو Failed |
| Pickup | Preparing → ReadyForPickup → PickedUp |

`Order.AdvanceFulfillment` يشترط أن OrderStatus = Confirmed. بعد تسليم الطلب أو استلامه، يقوم `CollectCodPayment` بتحويل PaymentStatus إلى Collected وOrderStatus إلى Completed.

## 4. تحرير المخزون

- Reject أو Cancel يحرر Reservations عبر `ReleaseOrderReservationsAsync`.
- تحصيل COD يثبت الحجز/الخصم عبر `CommitOrderReservationsAsync`.
- الطلبات Pending التي انتهى `ReservationExpiresAtUtc` يتم تحريرها دورياً بواسطة `MaintenanceBackgroundService` الذي يستدعي `OrderService.ReleaseExpiredReservationsAsync` كل دقيقة في Scope مستقل مع معاملة صريحة لكل طلب.

## 5. تاريخ الحالات

`OrderStatusHistory` يحفظ actor role وfrom/to status وnote ووقت UTC. `OrderDto` الحالي لا يعرض History collection في contract المعاين، لذلك وجود السجل في DB لا يعني أن العميل يستطيع قراءته عبر API.
