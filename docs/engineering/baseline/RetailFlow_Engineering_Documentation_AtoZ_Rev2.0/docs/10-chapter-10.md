# 10. وحدة المتاجر وإدارة المنصة

## 10.1 الحالات الحالية

Domain الحالي يحتوي `PendingApproval=1`, `Active=2`, `Suspended=3`, `Rejected=4`. Transition Matrix لإعادة تقديم طلب مرفوض أو إعادة تفعيل متجر معلّق لم تعتمد بعد.

## 10.2 النموذج الحالي

Store: Id, Name, Slug, StoreType, Status, CreatedAtUtc. StoreSettings: CurrencyCode, TimeZoneId, DeliveryEnabled, PickupEnabled. كيانات Branding والعناوين العامة والداخلية ليست كلها موجودة في Product schema المرجعي.

## 10.3 Onboarding Target Flow

1. تحقق هوية مقدم الطلب.
2. Normalize/validate Slug.
3. قيد تفرد في SQL.
4. إنشاء طلب PendingApproval.
5. إدخاله في Platform Admin queue.
6. قرار Approve/Reject مع actor/time/reason.
7. عند الموافقة تحقق الإعدادات اللازمة ثم Active.
8. Audit event لكل انتقال.

## 10.4 Slug governance

احسم الأحرف والـ Unicode والـ reserved names وتغيير slug وredirects وحجز الاسم القديم. قاعدة التفرد في SQL هي الحكم النهائي عند التزامن.

## 10.5 Branding والعناوين

افصل PublicStoreAddress عن FulfillmentAddress. لا تسرّب العنوان الداخلي إلى DTO عام. Media provider قرار مفتوح؛ يجب توثيق MIME/size/dimensions/scanning/storage naming/CDN/cache deletion/tenant prefix/retention.

## 10.6 Platform Admin security

BackOffice identity منفصلة؛ least privilege؛ support break-glass بسبب ومدة وصلاحية وتدقيق؛ لا يوجد Global Admin يتجاوز Tenant Isolation دون مسار واضح.
