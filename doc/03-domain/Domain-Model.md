# نموذج المجال (Domain Model)

## الكيانات الأساسية

| Aggregate/Entity | أهم الحقول | السلوك/الملاحظات |
|---|---|---|
| Store | Id, Name, Slug, StoreType, Status, Branding, CreatedAtUtc | Create, NormalizeSlug, UpdateBranding, Approve, Reject, Suspend |
| StoreSettings | StoreId, CurrencyCode, TimeZoneId, DeliveryEnabled, PickupEnabled, PublicAddress, FulfillmentAddress | يمنع Pickup وعنوان الزيارة العام للـ OnlineOnly |
| Currency | Code, NumericCode, MinorUnit, IsActive | تطبيع العملة والتحقق من دقة المبلغ |
| Category | StoreId, Name, Slug, IsActive, SortOrder | Create/Update |
| Product | StoreId, CategoryId, Name, Price, Status | تحقق من store/currency/amount، Publish/Archive/MoveToDraft |
| ProductImage | StoreId, ProductId, ImageUrl, AltText, SortOrder | HTTP/HTTPS absolute URL وحدود طول ونظام ترتيب |
| InventoryItem | StoreId, ProductId, OnHand, Reserved, RowVersion | Available = OnHand - Reserved؛ Reserve/Release/Commit/SetOnHand |
| DeliveryZone | StoreId, Name, NormalizedName, FlatFee, IsEnabled | رسم ثابت غير سالب بعملة المتجر |
| RetailFlowIdentityUser / AppRole | Id, StoreId?, Email, NormalizedEmail, IsActive, TwoFactorEnabled, MfaEnrollmentStatus | ASP.NET Core Identity (`AspNetUsers` + `AspNetRoles`: `Customer`, `MerchantOwner`, `PlatformAdmin`) |
| SessionTicket | Key, UserId, StoreId?, Value, CreatedAtUtc, ExpiresAtUtc, LastSeenAtUtc, AbsoluteExpiresAtUtc | server-side ticket مع idle وabsolute timeout |
| Cart / CartItem | StoreId, CustomerId, ProductId, Quantity, RowVersion | Cart واحدة لكل store/customer مع SQL `rowversion` للتزامن التفاؤلي |
| CheckoutRequest | StoreId, CustomerId, IdempotencyKey, RequestHash, Status, ClaimToken, LeaseExpiresAtUtc, OrderId, ExpiresAtUtc | Claim / Lease (60s) / Recovery / MarkCompleted / MarkFailed |
| Order | StoreId, CustomerId, OrderNumber, Status axes, totals, fulfillment details | Domain methods تتحكم بالانتقالات وتسجل History |
| OrderItem | Product snapshot, SKU, unit price, quantity, line total | يحفظ بيانات المنتج وقت الطلب |
| OrderStatusHistory | from/to statuses, actor, note, timestamp | يسجل الانتقالات |

## Invariants بارزة في Domain

- Store Id واسم المتجر وslug مطلوبة؛ slug يُطبع lower-case.
- لا يقبل Store إلا انتقالات lifecycle المعرفة في `Store`.
- إعدادات OnlineOnly لا تسمح Pickup أو public visit address.
- Product يتطلب StoreSettings وCurrency متطابقين مع StoreId/CurrencyCode، وسعر غير سالب ودقة متوافقة مع MinorUnit.
- Inventory لا يسمح بقيم سالبة ولا Reserved أعلى من OnHand.
- Cart quantity وOrderItem quantity يجب أن تكون موجبة.
- ProductImage URL يجب أن يكون HTTP أو HTTPS مطلقاً.
- DeliveryZone fee غير سالب ومتوافق مع عملة المتجر.
- Order transition methods ترفض انتقالات غير صالحة.

**CODE:** `src/RetailFlow.Domain/**` و`src/RetailFlow.Infrastructure/Persistence/Configurations/**`.

## ملاحظات تصميمية

1. `OrderStatus`, `FulfillmentStatus`, `PaymentStatus` محاور منفصلة، وليست Enum واحدة.
2. `OrderItem` يحتفظ باسم المنتج وSKU والسعر عند الطلب، فلا يعتمد عرض الطلب التاريخي على السعر الحالي للمنتج.
3. بعض invariants موجودة في Domain وبعضها في SQL constraints/unique indexes؛ يجب عدم إزالة أحد المستويين دون مراجعة.
4. لا يظهر في Product Host حتى baseline نموذج Refund أو ExternalPayment أو Shipment carrier.
