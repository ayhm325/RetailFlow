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
- **Claim & Lease & Transaction Boundaries (تم تحديثه في AUD-004):** الـ claim الأول (`CheckoutRequest`) يُحفظ في `SaveChangesAsync` أولى مع `ClaimToken` و`LeaseExpiresAtUtc` (`60s`) و`ExpiresAtUtc` (`24h`)، ويتم الاحتفاظ بهوية التنفيذ المستمرة عبر `ExecutionClaim(ClaimId, ClaimToken, RequestHash)` في `CheckoutService`. عند اكتمال الطلب داخل معاملة `ReadCommitted` صريحة، يُثبت `CompletedAtUtc = completionNow` و`ExpiresAtUtc = completionNow + 24h`. عند استرداد Lease منتهٍ يُطبَّق `Max(existing.ExpiresAtUtc, newLeaseExpiresAtUtc + 5m)` (`LeaseRecoveryRetentionGrace = 5m`). عند انتهاء `ExpiresAtUtc` (ومع عدم وجود Lease نشط)، يُسترد السجل المتقادم ذرياً في خطوة واحدة (`ExecuteUpdateAsync` Atomic Reclaim) مع تصفير حقول الجيل السابق، وفي حال عدم اليقين بعد محاولة الالتزام يُعاد التحقق في `DbContext` جديد عبر `ReconcileInNewScopeAsync` ومهلة مستقلة (`5s`) دون تعليم الطلب `Failed` أبداً.
- **Background Reservation Release & Bounded Cleanup (تم تحديثه في AUD-004):** `MaintenanceBackgroundService` مسجل في `ProductHostExtensions` ويعمل كل دقيقة عبر `PeriodicTimer` لاستدعاء `OrderService.ReleaseExpiredReservationsAsync` وتنظيف `CheckoutRequests` المنتهية بنمط Two-Step Bounded Cleanup (`CheckoutCleanupBatchSize = 500`, `MaxCheckoutCleanupBatchesPerSweep = 10`, مرتباً بـ `ExpiresAtUtc, Id` مع إعادة شرط الأهلية الكامل في `ExecuteDeleteAsync`) وتنظيف `SessionTickets` المنتهية.

## Open questions

ما هي semantics الصحيحة لمفتاح idempotency؟ هل يجب أن يدخل cart snapshot في request fingerprint؟ ما سياسة الاحتفاظ بالمفاتيح؟ كيف يُضمن تنفيذ تحرير الحجوزات تلقائياً؟
