# 06. معمارية البيانات والاستمرارية

## 06.1 الوضع الحالي

`RetailFlowDbContext` يحتوي حالياً DbSets: `Currencies`, `Stores`, `StoreSettings`, `Categories`, `Products`, `ProductImages`, `InventoryItems`. قاعدة المنتج المرجعية `RetailFlowProduct`. مصدر الاتصال: `RETAILFLOW_CONNECTION` ثم `ConnectionStrings:RetailFlow`، مع fallback محلي في Development حسب Resolver.

هذه Catalog Slice وليست Commerce Schema كاملة. جداول Customer/Cart/Orders/Reservations/DeliveryZones/StoreMembership/Production Identity ليست موجودة في Product Host baseline.

## 06.2 نموذج الجداول المستهدف

| المجال | الكيانات/الجداول | الحالة |
|---|---|---|
| Global reference | Currencies | موجودة |
| Stores | Stores, StoreSettings, StoreBranding, PublicStoreAddresses, FulfillmentAddresses | Store/Settings موجودة؛ الباقي مستهدف |
| Catalog | Categories, Products, ProductImages | موجودة |
| Inventory | InventoryItems, InventoryReservations, InventoryTransactions | InventoryItems موجود؛ الباقي مستهدف |
| Customers | CustomerProfiles, CustomerAddresses | مستهدف |
| Cart | Carts, CartItems | مستهدف |
| Orders | Orders, OrderItems, OrderStatusHistory | مستهدف |
| Fulfillment | DeliveryZones, DeliveryZoneAreas | مستهدف |
| Identity | AspNetUsers, StoreMemberships | PoC/design منفصل وليس Product Host |
| Sessions | AuthenticationSessionTickets, DataProtectionKeys | PoC design؛ ليس schema إنتاجياً معتمداً |
| Idempotency | CheckoutRequests أو request ledger equivalent | مقترح |

## 06.3 SQL constraints

- كل كيان Tenant-owned يحمل StoreId أو علاقة مركبة تضمن الملكية.
- Composite FK مثل `(StoreId, CategoryId)` إلى `(StoreId, Id)` في Category.
- Composite FK مثل `(StoreId, ProductId)` إلى Product.
- Unique StoreSlug على مستوى المنصة.
- SKU unique per Store عندما لا يكون NULL، باستخدام filtered unique index.
- Check constraints للسعر والكمية والحالات.
- `rowversion` concurrency token لا يمثل وقتاً تقويمياً.
- الفهارس مشتقة من الاستعلامات وخطط التنفيذ الفعلية، لا من التخمين.

## 06.4 Money وCurrency

Product الحالي يخزن Price بدقة `decimal(19,4)` ويطبق `MoneyPrecision` وفق `Currency.MinorUnit` دون rounding ضمني. Seed currencies التي فُحصت في الكود: JOD/KWD (3 minor units)، SAR/AED/USD/EUR/GBP (2)، JPY (0). وجود seed لا يعني أن JOD اعتمدت عملاً كعملة افتراضية لكل متجر.

كل Order تاريخي يجب أن يحفظ Currency وUnitPrice/fees snapshots حسب التصميم. لا تعتمد على السعر الحي للمنتج لتفسير طلب سابق. الضرائب والخصومات والفواتير لم تحددها وثائق MVP وتحتاج قراراً تجارياً وقانونياً.

## 06.5 Transaction Boundaries

Checkout المقترح يضم claim Idempotency، revalidation، إنشاء Order/Items، Reservation/Inventory update، Status History، وحفظ نتيجة المفتاح ضمن عملية ذرية واحدة. استخدم معاملات EF Core الافتراضية عندما تكفي؛ استخدم Transaction صريحة عند وجود عدة SaveChanges/أوامر يجب أن تكون وحدة متماسكة. لا تفتح transaction لكل عملية قراءة بلا داعٍ. راجع Savepoints وMARS عند استخدام SQL Server.

## 06.6 Concurrency

- لا تعتمد على قراءة Available ثم كتابة غير مشروطة.
- استخدم RowVersion أو conditional atomic update أو locking strategy مدروسة.
- تعامل مع `DbUpdateConcurrencyException` وSQL conflicts بشكل واضح.
- اختبر آخر قطعة بطلبات متزامنة على SQL Server حقيقي؛ EF InMemory لا يثبت سلوك SQL Server.
- اجعل Reservation الانتقالية Active -> Released/Consumed مرة واحدة فقط.

## 06.7 Idempotency

النطاق المقترح `(StoreId, CustomerId, IdempotencyKey)`، مع request fingerprint وoperation status وresponse reference. يجب حسم مدة الاحتفاظ، إعادة استخدام المفتاح مع body مختلف، الطلبات المتزامنة بنفس المفتاح، وسلوك retry بعد timeout. القيد الفريد في SQL هو خط الدفاع النهائي.

## 06.8 Migrations وBackup/Restore

- Migration الإنتاج لا تنسخ من PoC.
- اختبر fresh DB وupgrade من نسخة مماثلة للإنتاج.
- عند تغيير غير قابل للعكس، استخدم forward-fix plan بدلاً من وعد rollback غير واقعي.
- Backup encryption/retention/restore drill وRPO/RTO قرارات مفتوحة.
- Data Protection keys بيانات تشغيلية حرجة إذا اعتمدت Cookie sessions؛ يجب مشاركتها وحمايتها ونسخها قبل Production.

---
