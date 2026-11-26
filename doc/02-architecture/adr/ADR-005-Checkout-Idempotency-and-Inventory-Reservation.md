# ADR-005: Checkout idempotency وحجز المخزون

**Status:** Inferred from implementation — Needs Product and Engineering Approval

## Context

Checkout يمكن أن يصل أكثر من مرة بسبب إعادة المحاولة أو تزامن الطلبات. كما يجب ألا تتجاوز الكمية المحجوزة المتاح من المخزون.

## Observed decision

- يطلب endpoint header باسم `Idempotency-Key` وبحد أقصى 128 حرفاً.
- يُنشأ سجل CheckoutRequest فريد بحسب StoreId + CustomerId + IdempotencyKey.
- حالات السجل InProgress/Completed/Failed.
- المخزون يحتوي OnHand وReserved وAvailable ويُستخدم RowVersion.
- إنشاء Order وOrderItems وتحديث Inventory ومسح Cart واكتمال CheckoutRequest تُرسل في SaveChanges لاحقة واحدة.

## Evidence

`CustomerOrderEndpoints.cs`, `CheckoutService.cs`, `CheckoutRequest.cs`, `InventoryItem.cs`, `InventoryItemConfiguration.cs`, `CheckoutRequestConfiguration.cs`.

## Important implementation limits (Historical Baseline) & Current Reconciliation (2026-11-26)

> النقاط التالية توضح ما كان ملحوظاً في Baseline الأولي مقابل التنفيذ الفعلي الحالي في الكود:

- **Request Hash (تم إصلاحه في الكود):** `CheckoutService.ComputeRequestHash` يشمل الآن `StoreId` و`CustomerId` و`FulfillmentMode` و`DeliveryZoneId` و`DeliveryAddress` بالإضافة إلى `cartSignature` (`ProductId:Quantity` مرتبة)، ويعيد بناء التوقيع من `Order.Items` عند Replay.
- **Claim & Lease & Transaction Boundaries:** الـ claim الأول (`CheckoutRequest`) يُحفظ في `SaveChangesAsync` أولى مع `ClaimToken` و`LeaseExpiresAtUtc` (`60s`) و`ExpiresAtUtc` (`24h`)، وتنتهي معالجة الطلب وحجز المخزون وإفراغ السلة واكتمال الـ claim داخل معاملة `ReadCommitted` صريحة في `CheckoutService`. عند انتهاء `ExpiresAtUtc` يُحذف السجل المتقادم ويُسمح بإعادة استخدام المفتاح؛ وعند تعطل `InProgress` بعد انتهاء Lease يُسترد السجل ذرياً.
- **Background Reservation Release (تم تنفيذه في الكود):** `MaintenanceBackgroundService` مسجل في `ProductHostExtensions` ويعمل كل دقيقة عبر `PeriodicTimer` لاستدعاء `OrderService.ReleaseExpiredReservationsAsync` وتنظيف `CheckoutRequests` و`SessionTickets` المنتهية.

## Open questions

ما هي semantics الصحيحة لمفتاح idempotency؟ هل يجب أن يدخل cart snapshot في request fingerprint؟ ما سياسة الاحتفاظ بالمفاتيح؟ كيف يُضمن تنفيذ تحرير الحجوزات تلقائياً؟
