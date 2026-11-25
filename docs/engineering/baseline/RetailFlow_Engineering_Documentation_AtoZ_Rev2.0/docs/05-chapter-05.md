# 05. معمارية النظام ووصفه

## 05.1 الأهداف المعمارية

- منتج متعدد المتاجر بقاعدة تنفيذ مشتركة للـ MVP.
- عزل بيانات المستأجرين على الخادم وقاعدة البيانات.
- فصل Domain عن HTTP وEF Core وReact.
- تقليل التعقيد التشغيلي والتكلفة في البداية.
- السماح بتوسع مدروس دون Microservices مبكرة.

## 05.2 المعمارية الحالية في الكود

```text
React SPA (NOT PRESENT AT BASELINE)
             |
             v
RetailFlow.Api (ASP.NET Core .NET 10)
  | Public catalog endpoints
  | ProductHost middleware / health / ProblemDetails
             |
             v
RetailFlow.Application
  | StoreRequestContext + Catalog query contract
             |
             v
RetailFlow.Domain
  | Store, StoreSettings, Category, Product, InventoryItem, Currency
             ^
             |
RetailFlow.Infrastructure
  | EF Core, SQL Server, queries, migrations, store resolver
             |
             v
       SQL Server (RetailFlowProduct)
```

هذه صورة للـ Product Host الفعلي في commit المرجعي. Identity وOrders وCart وReact ليست ضمن هذه الصورة الحالية.

## 05.3 المعمارية المستهدفة

- `RetailFlow.Api`: Host وHTTP contract وcomposition root.
- `RetailFlow.Application`: use cases وports/contracts وتنسيق العمليات.
- `RetailFlow.Domain`: entities, value objects, invariants and domain behaviors.
- `RetailFlow.Infrastructure`: EF Core وSQL Server وexternal adapters.
- React + TypeScript SPA بنفس الـ Origin عند التنفيذ.
- SQL Server مشترك في MVP مع StoreId وComposite Constraints كخط أساس مقترح.

## 05.4 اتجاه الاعتمادات

اتجاه الاعتماد نحو الداخل: `Api -> Application -> Domain`، وInfrastructure يحقق Ports المطلوبة دون أن تعتمد Domain على EF Core أو ASP.NET Core أو React. يجب مراجعة Project References ومنع Application من استيراد Presentation. Composition Root هو المكان المناسب لتسجيل Infrastructure عبر DI.

## 05.5 Request Pipeline المستهدف

```text
Trusted Forwarded Headers
 -> Exception Handling / HSTS / HTTPS
 -> Routing
 -> Canonical Store Slug Resolution
 -> Browser CSRF enforcement for unsafe methods
 -> Identity Scope binding (server-derived AccountKind + StoreId)
 -> Cookie Authentication / ticket validation
 -> Authorization / membership / resource ownership
 -> Application Use Case
 -> Infrastructure / SQL Server
```

الترتيب النهائي يجب أن يطابق ASP.NET Core وقرارات CSRF، ويُثبت باختبارات integration/browser، خصوصاً Login/Register والـ public endpoints. لا تغير ترتيب Middleware دون اختبار أثره.

## 05.6 Tenant Isolation Defense in Depth

1. Tenant Resolution موثوق من Route/Host canonical.
2. Request-scoped Store Context واضح ومحدود العمر.
3. Application use cases تستقبل السياق صراحة.
4. Authorization يتحقق من AccountKind/StoreId/Membership/Resource.
5. EF query predicates أو Global Query Filters عند ملاءمتها.
6. Composite Foreign Keys وUnique Constraints في SQL.
7. Integration Tests على SQL Server الحقيقي.
8. مراجعة Raw SQL و`IgnoreQueryFilters` وBackground Jobs وPlatform Admin pathways.

Global Query Filters أو Route Slug أو Cookie Path أو إخفاء البيانات في الواجهة ليست حدوداً أمنية منفردة.

## 05.7 Architecture Description

تستخدم وثائق arc42 لعرض Goals, Constraints, Context, Solution Strategy, Building Blocks, Runtime, Deployment, Cross-cutting Concepts, ADRs, Quality Requirements, Risks, Glossary. تستخدم C4 لتصوير Context/Container/Component/Code عند الحاجة. ملفات Mermaid ضمن `diagrams/` هي مصادر قابلة للتحديث.

## 05.8 ADR governance

كل قرار معماري جوهري يجب أن يحتوي: ID، Status، Context، Decision، Alternatives، Consequences، Risks، Related Requirements، Implementation Status، Verification، Owner، Review date. القرار `Same-Origin` للـ MVP يختلف عن قرار Session Transport الذي بقي Proposed في الـ baseline.
