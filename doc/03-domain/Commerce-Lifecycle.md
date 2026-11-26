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

1. قراءة Cart الحالية.
2. إنشاء SHA-256 request hash من store/customer/fulfillment mode/delivery zone/address.
3. إدراج CheckoutRequest بحالة InProgress وحفظه.
4. عند unique conflict: تحميل السجل الموجود، ومقارنة hash، ثم إرجاع PayloadMismatch أو ConcurrentInFlight أو Replayed عند وجود Order مكتمل.
5. التحقق من أن المتجر Active وله Currency.
6. في Delivery، يشترط وجود DeliveryZoneId صالح ومفعّل ضمن المتجر نفسه.
7. جلب المنتجات مع Inventory وCategory والتحقق أنها Published وفئتها فعالة ولها Inventory.
8. حجز كمية كل منتج عبر InventoryItem.Reserve.
9. إنشاء Order وOrderItems، وتخزين delivery fee/zone name/address حيث يلزم.
10. إزالة CartItems وتغيير CheckoutRequest إلى Completed وحفظ التغييرات.

### نقاط مهمة

- يتم التحقق من المخزون عند Checkout، لا عند إضافة Cart.
- التحقق من DeliveryEnabled على StoreSettings يجب أن يُراجع مقابل كل مسار checkout؛ لا تستنتج أن وجود delivery zone وحده يغطي كل سياسة تفعيل.
- لا يتضمن request hash محتوى Cart والكميات حالياً. هذا فجوة مهمة في semantics idempotency.
- الـ claim الأول ليس ضمن SaveChanges نفسه الذي يحفظ Order والمخزون.
- لا يوجد job في Product Host يثبت استدعاء تحرير الحجوزات المنتهية بشكل دوري.

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
- الطلبات Pending التي انتهى `ReservationExpiresAtUtc` يمكن تحريرها بواسطة `ReleaseExpiredReservationsAsync`.
- لكن استدعاء هذه الدالة لا يظهر في Product Host registration/startup ولا HostedService في شجرة `src/`; لذلك لا تفترض أن الـ 15-minute timeout يعمل تلقائياً في بيئة التشغيل الحالية.

## 5. تاريخ الحالات

`OrderStatusHistory` يحفظ actor role وfrom/to status وnote ووقت UTC. `OrderDto` الحالي لا يعرض History collection في contract المعاين، لذلك وجود السجل في DB لا يعني أن العميل يستطيع قراءته عبر API.
