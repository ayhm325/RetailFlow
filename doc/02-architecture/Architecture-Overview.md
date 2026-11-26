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
- Identity: ASP.NET Core Identity (`RetailFlowIdentityUser`, `RetailFlowIdentityRole`, `ScopedUserStore`) و`SessionTicket` persistence.
- Orders: Cart, CheckoutRequest, Order/OrderItem/History.
- Fulfillment: DeliveryZone and fulfillment statuses.
- Currencies: currency code and money precision rules.

## 5. دورة الطلب

`Program.cs` يسجل Product Host infrastructure ثم Infrastructure services، يبني التطبيق، يطبق `UseProductHostPipeline`، ثم يربط مجموعات Auth/Public Catalog/Merchant/Customer Orders/Platform Admin وعمليات Host.

## 6. قاعدة البيانات

`RetailFlowDbContext` يضم DbSets للمتجر والإعدادات والعملة والفئات والمنتجات والصور والمخزون ومناطق التوصيل والمستخدمين والجلسات والسلة والطلبات وسجلات Checkout. يتم تحميل configurations تلقائياً من assembly.

## 7. ما لا يدخل في Product Host

مشروعا `IdentitySpike` و`SessionTransportSpike` لهما solutions وDbContexts/مشاريع مستقلة. CI يبنيهما ويختبرهما منفصلين. لا تعتمد على أي class في هذين المسارين باعتباره مستخدماً في Product Host ما لم يثبت ذلك من `Program.cs` وDI في المنتج.

## 8. حدود المعمارية الحالية والهدف المستقبلي

- **التنفيذ الحالي:** Product Host يخدم HTTP Minimal APIs فقط؛ لا يوجد React client project في tree حتى الآن.
- **البنية المستهدفة للواجهة (`ADR-007`):** استضافة React SPA مستقبلاً داخل ASP.NET Core Product Host نفسه بنمط Same-Origin.
- **العامل الخلفي والصيانة:** يوجد `MaintenanceBackgroundService` (`PeriodicTimer`) مسجلاً في `ProductHostExtensions` لتحرير حجوزات المخزون المنتهية وتنظيف `CheckoutRequests` و`SessionTickets` المنتهية؛ كما يوجد `PlatformBootstrapHostedService` لتهيئة أول `PlatformAdmin`.
- **حدود المعاملات:** في `CheckoutService` يُحفظ الـ claim (`CheckoutRequest`) أولاً في `SaveChangesAsync` مستقل (مع Lease 60s)، ثم تنفذ بقية العملية (Order + Inventory + Cart + Claim completion) داخل معاملة `ReadCommitted` صريحة.
- لا يظهر Outbox، message broker، distributed cache، external payment adapter، أو object storage للصور، ولا توجد ملفات نشر IaC في الشجرة.

هذه ملاحظات وصفية وليست توصية بأن النظام يجب أن يتبنى الخدمات الموزعة.
