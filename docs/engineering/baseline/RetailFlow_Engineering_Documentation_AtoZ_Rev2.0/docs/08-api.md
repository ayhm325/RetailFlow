# 08. معمارية API والعقود

## 08.1 قواعد العقد

- API عقد منتج مستقل عن Entities وEF Core.
- DTOs تمثل response/request واضحين.
- كل عملية تجارية تمر عبر Store scope موثوق.
- Validation errors تستخدم ProblemDetails عند ملاءمتها.
- لا تكشف الأخطاء stack trace أو SQL/secret data.
- لا تقبل السعر أو الإجمالي أو StoreId أو Claims من العميل كمصدر حقيقة.
- Pagination/search لها limits واضحة.

## 08.2 Endpoints الموجودة فعلياً

| Method | Route | Auth | الوصف |
|---|---|---|---|
| GET | `/api/stores/{slug}/catalog/categories` | Public | تصنيفات نشطة لمتجر نشط |
| GET | `/api/stores/{slug}/catalog/products` | Public | قائمة المنتجات المنشورة مع search/filter/sort/page |
| GET | `/api/stores/{slug}/catalog/products/{productId}` | Public | تفاصيل المنتج المنشور في المتجر الحالي |

في `CatalogProductsQuery`: Page=1، PageSize=20، MaxPageSize=50، MaxSearchLength=100. Sort: `newest`, `price_asc`, `price_desc`, `name_asc`, `name_desc`. ترتيب النتائج حتمي بإضافة ID عند التعادل.

## 08.3 سلوك API الحالي

- `StoreResolutionEndpointFilter` يحل Slug إلى Store نشط ويثبت `IStoreRequestContextAccessor`.
- `PublicCatalogQueryService` يستخدم `AsNoTracking()` وإسقاط DTOs.
- تظهر المنتجات المنشورة فقط، وتصنيفها يجب أن يكون نشطاً وينتمي إلى Store نفسه.
- التوفر العام مشتق من `InventoryItem.OnHand - Reserved > 0` عندما يوجد سجل مخزون.
- تفاصيل منتج خارج المتجر أو غير ظاهر تعيد 404 وفق المسار العام.
- لا توجد عمليات Merchant write أو Cart أو Checkout أو Orders في Product Host baseline.

## 08.4 APIs مستقبلية مقترحة وليست منفذة

**Customer:** Register/Login/Logout/Me/Profile/Addresses.  
**Cart:** Get cart, Add item, Update quantity, Remove item.  
**Checkout:** `POST /api/stores/{slug}/checkout` مع `Idempotency-Key`.  
**Merchant:** إدارة المنتجات والتصنيفات والمخزون والطلبات ومناطق التوصيل، مع `StoreMembership` وresource authorization.

لا تُدرج هذه المسارات في OpenAPI الحالي؛ ملف `api/openapi-current.yaml` يصف فقط الثلاثة الموجودة في المصدر.

## 08.5 Error and compatibility policy

- استخدم RFC 9457 Problem Details مع error codes مستقرة قدر الإمكان.
- 400 للمدخلات غير الصالحة، 401 للهوية المفقودة، 403 للمنع، 404 للمورد غير الموجود ضمن نطاق المتجر، 409 للتعارض أو idempotency conflict حسب contract.
- لا تغير DTO بصورة breaking دون قرار.
- ولّد OpenAPI في CI وقارن التغيرات.
- الملف الحالي يستخدم OpenAPI 3.1.1 للتوافق الواسع، ويحتاج validation مع generator الفعلي قبل إعلان دعم OpenAPI 3.2.x.
