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
| `20261126094533_AdoptAspNetCoreIdentity` | تحويل `AppUsers` إلى `AspNetUsers` مع الحفاظ على `Guid` وبيانات المستخدمين، وإضافة `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`, `AspNetRoleClaims` وتحويل الأدوار (`1/2/3`) |
| `20261126101201_AddIdentityMfaHardeningFields` | حقول حالة MFA في `AspNetUsers` (`MfaEnrollmentStatus`, `MfaPendingExpiresAtUtc`, `ProtectedPendingAuthenticatorKey`, `MfaFailedAttempts`, `MfaLockoutEndUtc`, `LastUsedTotpTimeStep`) |
| `20261126112151_AddCartAndCartItemRowVersion` | إضافة أعمدة `RowVersion` على `Carts` و`CartItems` للتزامن التفاؤلي |
| `20261126113905_AddSessionTicketAbsoluteLifetime` | إضافة `CreatedAtUtc` و`AbsoluteExpiresAtUtc` وفهرس انتهاء مطلق على `SessionTickets` |
| `20261127233914_AddFailedDeliveryLifecycleAndInventoryAudit` | إضافة `FulfillmentAttempts`, `FulfillmentAttemptItems`, `InventoryAdjustments` وحقول `Order.Version`/`InventoryReservationStatus` |
| `20261128001900_AddFulfillmentAttemptItemConcurrencyToken` | إضافة عمود `RowVersion` (`rowversion`) إلى `FulfillmentAttemptItems` للتزامن التفاؤلي |

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
8. الجداول التي تحتوي Filtered Unique Indexes (`UX_FulfillmentAttempts_*`, `UX_InventoryAdjustments_OrderItem_WriteOff`, `UX_Products_StoreId_Sku` وغيرها) تتطلب `SET QUOTED_IDENTIFIER ON` على الجلسة: هذا مضمون افتراضياً عبر `Microsoft.Data.SqlClient` / `dotnet ef database update`، أما عند تشغيل سكريبتات SQL يدوياً عبر `sqlcmd` فيجب تمرير الراية `-I`.

## نقطة تشغيلية

الـ CI يطبق migrations على SQL Server مؤقت، لكن لا يثبت ذلك وجود migration deployment process لقاعدة بيانات إنتاج دائمة. يلزم توثيق release owner وapproval وrollback strategy.
