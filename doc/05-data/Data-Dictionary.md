# قاموس البيانات

هذا قاموس منطقي للكيانات والحقول الرئيسية المستخرجة من Domain models وEF configurations. للحصول على DDL النهائي استخدم migrations على قاعدة البيانات المقصودة، ولا تعتبر هذا الملف بديلاً عن schema snapshot.

| Table / Entity | الأعمدة الأساسية | ضوابط/ملاحظات |
|---|---|---|
| Currencies | Code, NumericCode, MinorUnit, IsActive | Code 3 أحرف A-Z؛ NumericCode 3 أرقام؛ MinorUnit 0..4؛ بيانات seed عبر CurrencyConfiguration |
| Stores | Id, Name, Slug, StoreType, Status, branding/contact, CreatedAtUtc | unique slug؛ StoreType/Status check constraints |
| StoreSettings | Id, StoreId, CurrencyCode, TimeZoneId, DeliveryEnabled, PickupEnabled, PublicAddress, FulfillmentAddress | unique StoreId؛ علاقة بالمتجر والعملة |
| Categories | Id, StoreId, Name, Slug, IsActive, SortOrder | alternate key (StoreId, Id)، unique slug per store |
| Products | Id, StoreId, CategoryId, Name, Description, Sku, Price, Status, CreatedAtUtc | alternate key (StoreId, Id)؛ price>=0؛ indexes للـ status/category/sort/price؛ unique SKU per store when present |
| ProductImages | Id, StoreId, ProductId, ImageUrl, AltText, SortOrder | ترتبط بالمنتج عبر StoreId+ProductId |
| InventoryItems | Id, StoreId, ProductId, OnHand, Reserved, RowVersion | OnHand>=0, Reserved>=0, Reserved<=OnHand؛ unique per store/product |
| DeliveryZones | Id, StoreId, Name, NormalizedName, FlatFee, IsEnabled, CreatedAtUtc | flat fee>=0؛ unique normalized name per store |
| AppUsers | Id, StoreId?, Email, NormalizedEmail, DisplayName, PasswordHash, Role, IsActive, CreatedAtUtc | Role check 1..3؛ unique platform email where StoreId null؛ unique email within store where not null |
| SessionTickets | Key, UserId, StoreId?, Value, ExpiresAtUtc, LastSeenAtUtc, CreatedAtUtc, AbsoluteExpiresAtUtc | Key primary key؛ indexes على ExpiresAtUtc وAbsoluteExpiresAtUtc؛ Value varbinary(max) |
| Carts | Id, StoreId, CustomerId, UpdatedAtUtc, RowVersion | unique StoreId+CustomerId؛ alternate key StoreId+Id؛ `rowversion` للتزامن التفاؤلي على مستوى Aggregate |
| CartItems | Id, StoreId, CartId, ProductId, Quantity, RowVersion | Quantity>0؛ unique StoreId+CartId+ProductId؛ `rowversion` للتزامن التفاؤلي |
| CheckoutRequests | Id, StoreId, CustomerId, IdempotencyKey, RequestHash, Status, OrderId?, ResponseStatusCode?, CreatedAtUtc, ExpiresAtUtc | IdempotencyKey<=128؛ RequestHash char(64)؛ unique store/customer/key |
| Orders | Id, StoreId, CustomerId, OrderNumber, FulfillmentMode, OrderStatus, FulfillmentStatus, PaymentStatus, CurrencyCode, Subtotal, DeliveryFee, Total, delivery info, timestamps, RowVersion | unique StoreId+OrderNumber؛ check enums/totals؛ alternate key StoreId+Id |
| OrderItems | Id, StoreId, OrderId, ProductId, ProductName, Sku, UnitPrice, Quantity, LineTotal | quantity>0 والأسعار غير سالبة؛ snapshot product fields |
| OrderStatusHistories | Id, StoreId, OrderId, ActorUserId?, ActorRole, from/to statuses, Note, OccurredAtUtc | سجل انتقالات مرتبط بالطلب عبر StoreId+OrderId |

## بيانات حساسة أو تشغيلية

- `AppUsers.PasswordHash`: لا تسجلها في logs ولا expose في DTO.
- `SessionTickets.Value`: serialized AuthenticationTicket؛ عاملها كبيانات حساسة وقيّد الوصول إلى قاعدة البيانات والنسخ الاحتياطية.
- `StoreSettings.FulfillmentAddress`: موصوفة في domain بأنها داخلية ولا يجب إظهارها في responses العامة؛ اختبارات Public Store Identity تتأكد من عدم كشفها.
- `Orders.DeliveryAddress`: عنوان يقدمه العميل ويجب التعامل معه كبيانات شخصية/حساسة.

## مراجعات مطلوبة

تحقق من سياسة الاحتفاظ بالطلبات والجلسات وسجلات الحالة، ومن تشفير النسخ الاحتياطية، ومن صلاحيات DB users، ومن عدم إدخال PII في telemetry.
