# EF Core Migrations

## تسلسل migrations المرصود

بحسب أسماء الملفات في `src/RetailFlow.Infrastructure/Persistence/Migrations/`:

| Migration | الغرض المرصود |
|---|---|
| `20261125123923_InitialCatalogSlice` | Currencies, Stores, StoreSettings, Categories, Products, InventoryItems, ProductImages + القيود والفهارس والبذور |
| `20261125130511_HardenCurrencyConstraints` | تشديد قيود العملة |
| `20261125232250_AddStoreBrandingAddressesAndDeliveryZones` | حقول branding/contact وعناوين StoreSettings وجدول DeliveryZones |
| `20261125234754_AddCartOrdersAndCheckoutIdempotency` | Cart/CartItems/CheckoutRequests/Orders/OrderItems/OrderStatusHistories |
| `20261125234930_AddIdentityAndSessionTickets` | AppUsers وSessionTickets |
| `20261126085623_AddDataProtectionKeys` | DataProtectionKeys (`IDataProtectionKeyContext`) |
| `20261126092137_AddCheckoutRequestLeaseAndRecoveryFields` | CheckoutRequests (`ClaimToken`, `LeaseExpiresAtUtc`, `LastAttemptAtUtc`, `CompletedAtUtc`) |

كل migration لها Designer file، ويوجد `RetailFlowDbContextModelSnapshot.cs`.

## تشغيل migrations

من جذر المستودع بعد ضبط الاتصال:

```bash
dotnet tool restore

dotnet ef migrations list \
  --project src/RetailFlow.Infrastructure \
  --startup-project src/RetailFlow.Api \
  --context RetailFlowDbContext

dotnet ef database update \
  --project src/RetailFlow.Infrastructure \
  --startup-project src/RetailFlow.Api \
  --context RetailFlowDbContext
```

الأمر يفترض وجود `dotnet-ef` متاحاً محلياً/عبر tool manifest أو global tool متوافق. لا يوجد دليل على وجود tool manifest خاص بالمنتج في الشجرة؛ الـ CI يستعيد local tools من manifest تحت `experiments/IdentitySpike`، لذلك تحقق من إعداد dotnet-ef محلياً قبل نسخ الأمر كما هو.

## قواعد إدارة Migration

1. لا تعدّل migration مطبقة على بيئة مشتركة دون خطة تصحيح صريحة.
2. راجع SQL script الناتج قبل الإنتاج.
3. خذ backup وجرّب restore قبل ترقية schema ذات أثر كبير.
4. اجعل migration idempotency وdata backfill واضحين.
5. نفّذ integration tests على نسخة SQL Server مماثلة لبيئة التشغيل.
6. افحص lock duration والفهارس وأثر عمليات ALTER TABLE الكبيرة.
7. لا تستخدم `EnsureCreated` كبديل عن migrations في المسار الإنتاجي.

## نقطة تشغيلية

الـ CI يطبق migrations على SQL Server مؤقت، لكن لا يثبت ذلك وجود migration deployment process لقاعدة بيانات إنتاج دائمة. يلزم توثيق release owner وapproval وrollback strategy.
