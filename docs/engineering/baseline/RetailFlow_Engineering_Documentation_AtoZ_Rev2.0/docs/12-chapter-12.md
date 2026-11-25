# 12. المخزون والحجوزات والتزامن

## 12.1 الوضع الحالي

`InventoryItem` يحمل StoreId/ProductId/OnHand/Reserved/RowVersion. قاعدة البيانات تفرض OnHand>=0 وReserved>=0 وReserved<=OnHand. يوجد composite FK إلى Product. لا يوجد Reservation workflow أو Inventory Transaction ledger أو تعديل مخزون عبر API في Product Host baseline.

## 12.2 النموذج المستهدف

`InventoryReservation`: StoreId, OrderId, ProductId, Quantity, Status, CreatedAtUtc, ExpiresAtUtc، ReleasedAtUtc/ConsumedAtUtc بحسب التصميم. الحالات المرشحة Active/Released/Consumed/Expired. `InventoryTransaction` سجل append-only لكل تغيير مخزون مع Actor وReason وReference وCorrelation.

## 12.3 عملية الحجز

1. تحقق من Product/Store/Status.
2. تحقق من الكمية والطلب.
3. نفذ تحديثاً شرطياً ذرياً يمنع تجاوز Available.
4. أنشئ Reservation ضمن نفس transaction.
5. اكتب InventoryTransaction.
6. عند الفشل أعد كل الآثار أو استخدم compensation موثقاً.

## 12.4 استراتيجية التزامن

الاختيارات: RowVersion optimistic concurrency، أو conditional SQL update، أو locking strategy. القرار النهائي يقاس وفق contention/load. وجود RowVersion في Entity لا يكفي ما لم يستخدم في update path وتوجد اختبارات تعارض.

## 12.5 Reservation expiry قرار مفتوح

حدد وقت بدء الحجز ومدته، هل الحجز في Cart أم Checkout فقط، سلوك فشل العملية، خدمة التحرير، التعارض مع cancellation/confirmation، والتنظيف الآمن بعد توقف الخدمة. اجعل expiry idempotent.

## 12.6 Stock adjustments

كل تعديل يدوي يتضمن actor/reason/source/old/new quantity. لا تعدل Reserved مباشرة دون معالجة حجوزات. وفر reconciliation بين InventoryItem وActive Reservations.

## 12.7 اختبارات حاسمة

آخر قطعة وطلبان متزامنان؛ rollback إذا فشل أي item؛ duplicate checkout؛ duplicate cancellation؛ expiry vs confirm race؛ rowversion lost update؛ إعادة محاولة آمنة بعد deadlock/timeout.
