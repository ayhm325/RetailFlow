# مرجع API المستخرج من Endpoint Mapping

**Base path:** `/api`  
**Authentication:** ASP.NET Core Cookie Authentication، باستثناء المسارات العامة وعمليات الدخول/التسجيل.  
**OpenAPI:** يُعرض من `MapOpenApi()` فقط في Development؛ لا توجد OpenAPI artifact ثابتة شوهدت في المستودع.

## 1. Public Store API

كل المسارات التالية تحل `{slug}` إلى Store نشط من خلال `StoreResolutionEndpointFilter` ولا تتطلب Authorization policy.

| Method | Path | الغرض |
|---|---|---|
| GET | `/api/stores/{slug}` | هوية المتجر العامة وbranding وخيارات التوصيل/الاستلام |
| GET | `/api/stores/{slug}/delivery-zones` | مناطق التوصيل المفعّلة |
| GET | `/api/stores/{slug}/catalog/categories` | الفئات النشطة |
| GET | `/api/stores/{slug}/catalog/products` | قائمة المنتجات مع query params |
| GET | `/api/stores/{slug}/catalog/products/{productId}` | تفاصيل منتج Published |

`GET products` query parameters: `page` default 1، `pageSize` default 20 وبحد أقصى 50، `search` بحد أقصى 100 char، `categoryId`, `categorySlug`, `sort` من `newest|price_asc|price_desc|name_asc|name_desc`.

## 2. Store Authentication

| Method | Path | الغرض | الحماية |
|---|---|---|---|
| POST | `/api/stores/{slug}/auth/register` | تسجيل Customer جديد بالمتجر | Store resolution |
| POST | `/api/stores/{slug}/auth/login` | دخول مستخدم المتجر | Store resolution |
| POST | `/api/stores/{slug}/auth/logout` | إنهاء الجلسة | Authenticated |
| GET | `/api/stores/{slug}/auth/me` | بيانات المستخدم الحالي | Authenticated + tenant claim check داخل handler |

## 3. Platform API

| Method | Path | الغرض | الحماية |
|---|---|---|---|
| POST | `/api/platform/auth/login` | دخول PlatformAdmin | Credentials |
| POST | `/api/platform/auth/logout` | إنهاء جلسة PlatformAdmin | `PlatformAdmin` |
| POST | `/api/platform/stores` | إنشاء متجر | `PlatformAdmin` |
| POST | `/api/platform/stores/{storeId}/approve` | اعتماد/إعادة تنشيط متجر | `PlatformAdmin` |
| POST | `/api/platform/stores/{storeId}/reject` | رفض متجر PendingApproval | `PlatformAdmin` |
| POST | `/api/platform/stores/{storeId}/suspend` | تعليق متجر Active | `PlatformAdmin` |
| POST | `/api/platform/stores/{storeId}/merchant-owner` | إنشاء حساب MerchantOwner لمتجر | `PlatformAdmin` |

## 4. Merchant API

Group: `/api/stores/{slug}/merchant`، يتطلب `MerchantOwner` ثم Store resolution و`TenantClaimMatchEndpointFilter`.

| Method | Suffix | الغرض |
|---|---|---|
| PUT | `/branding` | تحديث اسم/شعار/لون/بريد/هاتف المتجر |
| PUT | `/addresses` | تحديث public/fulfillment addresses |
| POST | `/delivery-zones` | إنشاء منطقة توصيل |
| PUT | `/delivery-zones/{zoneId}` | تعديل رسم/تفعيل المنطقة |
| POST | `/categories` | إنشاء فئة |
| PUT | `/categories/{categoryId}` | تحديث فئة |
| POST | `/products` | إنشاء منتج ومخزون أولي |
| PUT | `/products/{productId}` | تحديث بيانات المنتج |
| POST | `/products/{productId}/publish` | نشر منتج |
| POST | `/products/{productId}/archive` | أرشفة منتج |
| PUT | `/products/{productId}/inventory` | تعديل OnHand |
| POST | `/products/{productId}/images` | إضافة صورة |
| DELETE | `/products/{productId}/images/{imageId}` | حذف صورة |
| GET | `/orders` | قائمة طلبات المتجر |
| POST | `/orders/{orderId}/confirm` | تأكيد طلب |
| POST | `/orders/{orderId}/reject` | رفض طلب |
| POST | `/orders/{orderId}/fulfillment` | دفع FulfillmentStatus للانتقال التالي |
| POST | `/orders/{orderId}/collect-payment` | تحصيل COD |

## 5. Customer commerce API

Group: `/api/stores/{slug}`، يتطلب `Customer` وStore resolution ومطابقة tenant claim.

| Method | Path | الغرض |
|---|---|---|
| GET | `/cart` | قراءة Cart |
| PUT | `/cart/items` | إضافة/تعديل كمية منتج |
| DELETE | `/cart/items/{productId}` | حذف منتج من Cart |
| DELETE | `/cart` | إفراغ Cart |
| POST | `/checkout` | تنفيذ Checkout مع `Idempotency-Key` |
| GET | `/orders` | قائمة طلبات Customer الحالي |
| GET | `/orders/{orderId}` | قراءة طلب يملكه Customer الحالي |
| POST | `/orders/{orderId}/cancel` | إلغاء طلب Pending يملكه Customer |

## 6. Host endpoints

| Method | Path | الغرض |
|---|---|---|
| GET | `/health/live` | فحص بقاء العملية دون dependency check |
| GET | `/health/ready` | فحص الجاهزية مع قاعدة البيانات |
| GET | `/openapi/v1.json` | OpenAPI في Development فقط |

## 7. ملاحظات عقد API

- أسماء endpoint المعيّنة بـ `WithName` موجودة في بعض Public/Auth routes وليست موحدة في جميع المسارات.
- لا توجد API version prefix مثل `/v1` في المسارات المعاينة.
- لم يظهر pagination على customer/merchant order lists أو merchant product lists في العقود المعاينة.
- يجب توليد OpenAPI من التطبيق بعد تشغيله في Development والتحقق من request/response schemas؛ الجداول أعلاه توثق routes ولا تُغني عن OpenAPI contract.
