# 11. وحدة الكتالوج والمنتجات

## 11.1 النموذج الحالي

Category(StoreId, Name, Slug, IsActive, SortOrder)، Product(StoreId, CategoryId, Name, Description, Sku, Price, Status, CreatedAtUtc)، ProductImage، وInventoryItem navigation. الحالات Draft/Published/Archived.

## 11.2 قواعد الكتالوج

- Category/Product من Store واحد.
- SKU اختياري لكنه فريد داخل Store عندما يوجد.
- Price غير سالب ويتوافق مع Currency Minor Unit.
- لا Variants في MVP.
- ProductImage لها ترتيب حتمي وAltText عند الحاجة.
- لا تعرض metadata داخلية أو مسارات تخزين حساسة.

## 11.3 Merchant CRUD Target

Create/Update/Publish/Archive Product، وإدارة Category، كل عملية تتحقق من Store context، Category، Currency، SKU، price precision، صور المنتج والحالة. أرشفة المنتج لا تغير Order snapshots القديمة.

## 11.4 Search

الموجود حالياً Contains على Name/Description/SKU مع Pagination/Sort. يجب اختبار collation وArabic normalization والـ case/diacritics وخطط التنفيذ. لا تنتقل إلى Full Text Search دون قياس ومتطلبات.

## 11.5 Media management open design

اختيار provider، allowed MIME types، max bytes، dimensions، content sniffing، malware scan، object naming، tenant namespace، CDN cache invalidation، orphan cleanup، deletion and retention.
