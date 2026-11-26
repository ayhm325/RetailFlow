# نظرة المعمارية

## 1. النمط العام

التطبيق الأساسي يتبع Layered Architecture ضمن توزيع مشاريع .NET، مع ASP.NET Core Minimal API كـ HTTP Host، وDomain Model يحتوي قواعد وسلوكاً، وApplication يحتوي العقود والواجهات، وInfrastructure ينفذ الخدمات ويصل إلى SQL Server باستخدام EF Core.

هذا ليس Microservices architecture: الحل الأساسي يشغّل Product Host واحداً ويتشارك `RetailFlowDbContext` وقاعدة SQL Server واحدة. الأنسب وصفه كتطبيق Modular/Layered Host بحدود Modules حسب المجالات، وليس إعلان Modular Monolith مكتمل دون اتفاق على حدود الوحدات والاستقلالية.

## 2. اتجاه الاعتماد

```text
RetailFlow.Api  ───────────────► RetailFlow.Application
      │                                │
      └────────► RetailFlow.Infrastructure
                                       │
RetailFlow.Infrastructure ────────────► Application contracts
RetailFlow.Infrastructure ────────────► RetailFlow.Domain
RetailFlow.Application ───────────────► RetailFlow.Domain
```

- `Domain` لا يعتمد على بقية مشاريع المنتج.
- `Application` يعتمد على `Domain` ويعرّف interfaces/contracts.
- `Infrastructure` يعتمد على `Application` و`Domain` ويحتوي EF Core/services/DI.
- `Api` يعتمد على `Application` و`Infrastructure` لربط الطلبات بالخدمات والـ middleware.

**CODE:** ملفات `*.csproj` الأربعة و`RetailFlow.slnx`.

## 3. المكونات

| المشروع | المسؤولية | أمثلة |
|---|---|---|
| API | Routes, middleware, authentication integration, hosting | `Program.cs`, `ProductHostExtensions`, endpoint groups |
| Application | Commands/DTOs/service interfaces | `OrderContracts`, `CatalogContracts`, `IAuthService` |
| Domain | Entities, enums, invariants, lifecycle methods | `Store`, `Product`, `InventoryItem`, `Order` |
| Infrastructure | Persistence, implementations, DI | `RetailFlowDbContext`, services, configurations, migrations |

## 4. تنظيم المجال داخل التنفيذ

- Stores: Store lifecycle, StoreSettings, tenancy context.
- Catalog: Category, Product, ProductImage, public/merchant catalog services.
- Inventory: InventoryItem and reservation semantics.
- Identity: AppUser roles and SessionTicket persistence.
- Orders: Cart, CheckoutRequest, Order/OrderItem/History.
- Fulfillment: DeliveryZone and fulfillment statuses.
- Currencies: currency code and money precision rules.

## 5. دورة الطلب

`Program.cs` يسجل Product Host infrastructure ثم Infrastructure services، يبني التطبيق، يطبق `UseProductHostPipeline`، ثم يربط مجموعات Auth/Public Catalog/Merchant/Customer Orders/Platform Admin وعمليات Host.

## 6. قاعدة البيانات

`RetailFlowDbContext` يضم DbSets للمتجر والإعدادات والعملة والفئات والمنتجات والصور والمخزون ومناطق التوصيل والمستخدمين والجلسات والسلة والطلبات وسجلات Checkout. يتم تحميل configurations تلقائياً من assembly.

## 7. ما لا يدخل في Product Host

مشروعا `IdentitySpike` و`SessionTransportSpike` لهما solutions وDbContexts/مشاريع مستقلة. CI يبنيهما ويختبرهما منفصلين. لا تعتمد على أي class في هذين المسارين باعتباره مستخدماً في Product Host ما لم يثبت ذلك من `Program.cs` وDI في المنتج.

## 8. حدود المعمارية الحالية

- لا يوجد React client project في tree.
- توجد معاملة EF ضمن `SaveChanges` الواحدة؛ العمليات متعددة المراحل عبر أكثر من `SaveChanges` يجب تحليلها بعناية. Checkout يسجل claim أولاً في SaveChanges مستقل، ثم يحفظ order/inventory/cart/claim completion لاحقاً.
- لا يظهر Outbox, background worker, message broker, distributed cache, external payment adapter أو storage abstraction للصور.
- لا توجد وثيقة نشر Infrastructure-as-Code في شجرة baseline.

هذه ملاحظات وصفية وليست توصية بأن النظام يجب أن يتبنى الخدمات الموزعة.
