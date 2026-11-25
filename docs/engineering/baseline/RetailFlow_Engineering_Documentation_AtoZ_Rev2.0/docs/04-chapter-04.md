# 04. نموذج المجال وقواعد الأعمال

## 04.1 نمط التصميم

RetailFlow يستهدف **Modular Monolith**: تطبيق ونشر مشترك مع وحدات مجال ذات حدود واضحة. لا تُنشأ Microservices لمجرد فصل المجلدات. يستخدم DDD لتحديد المسؤوليات وAggregates وInvariants حيث يضيف قيمة فعلية، وليس لفرض طبقات زائدة.

## 04.2 وحدات المجال

| الوحدة | المسؤولية | الكيانات المستهدفة |
|---|---|---|
| Identity & Access | الحسابات والعضويات والمصادقة والصلاحيات | ApplicationUser, StoreMembership, CustomerAccount |
| Stores | المتجر والحالة والهوية والإعدادات والعناوين | Store, StoreSettings, StoreBranding, Addresses |
| Catalog | المنتجات والتصنيفات والصور والنشر | Category, Product, ProductImage |
| Inventory | الكميات والحجوزات والتسويات والتزامن | InventoryItem, InventoryReservation, InventoryTransaction |
| Customers & Cart | ملف العميل والعناوين والسلة | CustomerProfile, CustomerAddress, Cart, CartItem |
| Orders | الطلب والتفاصيل والحالات والسجل | Order, OrderItem, OrderStatusHistory |
| Fulfillment | مناطق التوصيل ورسومها وخيارات التنفيذ | DeliveryZone, DeliveryZoneArea |

هذه وحدات منطقية داخل Modular Monolith وليست خدمات منفصلة.

## 04.3 ملكية البيانات

أي كيان تجاري خاص بمتجر يحمل `StoreId` أو يرتبط بعلاقة مركبة تضمن تطابق متجر الأصل. لا يكفي FK إلى ProductId وحده إذا أمكن ربط Order من متجر A بمنتج من متجر B.

- Store owns Products, Categories, Inventory and DeliveryZones.
- CustomerAccount belongs to one Store في MVP.
- Cart belongs to Customer + Store.
- Order belongs to Customer + Store.
- OrderItem references a product from the same Store ويحفظ snapshots تاريخية.
- InventoryReservation references Order and Product داخل Store نفسه.

## 04.4 Domain Invariants

| ID | القاعدة الثابتة |
|---|---|
| ST-01 | StoreSlug فريد على مستوى المنصة |
| ST-02 | المتجر غير النشط لا يستقبل عمليات شراء جديدة |
| ST-03 | Pickup يتطلب Physical + PickupEnabled |
| ST-04 | FulfillmentAddress لا يظهر عنواناً عاماً للزيارة |
| PR-01 | Product وCategory تابعان للمتجر نفسه |
| PR-02 | Draft وArchived لا يمكن شراؤهما |
| PR-03 | SKU اختياري وفريد داخل المتجر عند وجوده |
| IN-01 | OnHand وReserved لا يقلان عن صفر |
| IN-02 | Reserved لا يتجاوز OnHand |
| IN-03 | مجموع الحجوزات الفعالة لا يتجاوز المتاح قبل الحجز |
| IN-04 | Reservation لا يحرر أو يستهلك مرتين |
| OR-01 | Order يخص Store وCustomer متوافقين |
| OR-02 | Order يحتوي Item واحداً على الأقل |
| OR-03 | الخادم يعيد حساب السعر والرسوم والإجمالي |
| OR-04 | OrderItem يحتفظ باسم وسعر المنتج وقت الشراء |
| OR-05 | انتقال Order يتبع State Machine معتمدة |
| OR-06 | لا يتحول Completed إلى Cancelled في المسار الطبيعي |
| OR-07 | Checkout idempotent ضمن Store/Customer/Key |
| OR-08 | Cancellation يحرر Reservation مرة واحدة إذا سمحت الحالة |
| OR-09 | Payment Collected لا يسجل قبل تأكيد التحصيل |

## 04.5 Aggregate Boundaries المقترحة

- Store Aggregate: Store مع إعداداته التي يجب أن تتغير ذرياً؛ لا تضع الطلبات أو كامل المنتجات داخل Aggregate نفسه.
- Product Aggregate: Product + Images. Category غالباً مرجع لا ابن داخل Aggregate.
- Inventory Aggregate: InventoryItem/Stock ledger مع نقطة تزامن واضحة.
- Cart Aggregate: Cart + CartItems.
- Order Aggregate: Order + OrderItems + StatusHistory.
- DeliveryZone Aggregate: Zone + Areas.
- StoreMembership: حد عضوية مستقل User/Store/Role/Status.

هذه حدود مقترحة وتحتاج مراجعة عند تصميم Use Cases الفعلية.

## 04.6 Value Objects مرشحة

`Money`, `CurrencyCode`, `StoreSlug`, `PostalAddress`, `OrderNumber`, `IdempotencyKey`, `Quantity`, `SessionScope`. لا يخزن Money دون Currency context ودقة Minor Unit.

## 04.7 فصل الحالات

يجب فصل:

- `OrderStatus`: PendingConfirmation / Confirmed / Cancelled / Completed حسب سياسة المجال النهائية.
- `FulfillmentStatus`: NotStarted / Processing / ReadyForPickup / OutForDelivery / Delivered / PickedUp / Cancelled.
- `PaymentStatus`: PendingCollection / Collected في COD baseline.

يجب اعتماد Transition Matrix يحدد الحالات المسموحة، Actor المصرح، Preconditions، الآثار على المخزون والتحصيل، والاختبارات. المخطط في `diagrams/order-state.mmd` تصوري وليس قراراً نهائياً.

## 04.8 المخزون

`Available = OnHand - Reserved`. الحجز يزيد Reserved؛ الاستهلاك عند التسليم/الاستلام يخفض OnHand وReserved؛ الإلغاء المسموح يخفض Reserved فقط. يوصى بسجل `InventoryTransaction` append-only لكل adjustment/reservation/release/consumption/correction.

## 04.9 قرارات المجال المفتوحة

انتهاء Reservation؛ سياسة الإلغاء حسب الحالة والفاعل؛ تعريف Completion وعلاقته بتحصيل COD؛ صلاحيات Staff؛ اعتماد JOD الافتراضي؛ قرار Shared Schema النهائي بعد مراجعة المخاطر والتشغيل.
