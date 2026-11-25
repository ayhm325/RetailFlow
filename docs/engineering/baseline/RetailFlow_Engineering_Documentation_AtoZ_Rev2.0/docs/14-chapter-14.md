# 14. التجهيز والتوصيل والاستلام والدفع

## 14.1 طرق التنفيذ

Delivery يتطلب عنواناً وتحقق تغطية المنطقة ورسومها. StorePickup يتطلب متجر Physical مع PickupEnabled. OnlineOnly لا يدعم Pickup وفق قاعدة Settings في Domain.

## 14.2 Delivery Zones

النموذج المقترح DeliveryZone(StoreId, Name, FlatFee, IsActive) وDeliveryZoneArea(StoreId, DeliveryZoneId, AreaName). يجب حسم تطبيع أسماء المناطق والتداخل ورسوم الطلب التاريخي وسلوك تعطيل المنطقة.

## 14.3 Fulfillment transitions

افصل Processing/ReadyForPickup/OutForDelivery/Delivered/PickedUp. لا تسمح لمسار Pickup بالانتقال إلى OutForDelivery. لا تقبل الانتقال دون actor وpreconditions واضحة.

## 14.4 COD

COD هو خيار الدفع الأول لتقليل تكاملات الطرف الثالث. حدد من يثبت التحصيل، متى، وكيف يتم التعامل مع عدم وجود العميل أو رفض استلام الطلب أو التحصيل الجزئي. لا توجد Payment Gateway في MVP الأصلي.

## 14.5 Notifications

لا يوجد مزود محدد في المصدر. قبل التنفيذ حدد Email/SMS/WhatsApp، الأسعار، templates، retry/deduplication، delivery status، PII content، retention، fallback. لا تجعل إرسال إشعار غير أساسي يفشل عملية إنشاء Order؛ استخدم Outbox/job عندما تبرره متطلبات موثقة.

---
