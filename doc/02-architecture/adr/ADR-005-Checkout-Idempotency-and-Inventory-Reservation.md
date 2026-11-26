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

## Important implementation limits

- hash الحالي لا يتضمن بنود Cart والكميات؛ بل يشمل StoreId وCustomerId وFulfillmentMode وDeliveryZoneId وDeliveryAddress.
- claim الأول يُحفظ في SaveChanges منفصلة عن حفظ الطلب.
- ExpiresAtUtc مسجل 24 ساعة، لكن لا يظهر في الخدمة مسار تنظيف/إعادة استخدام بعد الانتهاء.
- لا يوجد scheduler أو hosted worker ظاهر في Product Host يستدعي `ReleaseExpiredReservationsAsync`.

## Open questions

ما هي semantics الصحيحة لمفتاح idempotency؟ هل يجب أن يدخل cart snapshot في request fingerprint؟ ما سياسة الاحتفاظ بالمفاتيح؟ كيف يُضمن تنفيذ تحرير الحجوزات تلقائياً؟
