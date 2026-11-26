# ADR-003: Store-scoped tenancy

**Status:** Inferred from implementation — Needs Approval

## Context

تخدم API عدة متاجر باستخدام route slug، ويجب عزل بيانات كل متجر عن الآخر.

## Observed decision

- store slug في مسار الطلب يحدد المتجر النشط.
- `StoreRequestContext` يحمل StoreId/slug/type/currency.
- خدمات القراءة والكتابة تقيد عملياتها بـ StoreId.
- Product/Category/Order children يستخدمون قيود وعلاقات مركبة تتضمن StoreId في مواضع متعددة.
- المستخدم المتجرّي يحمل `store_id` claim، وتتم مطابقته بالمتجر المحلول في بعض endpoint groups.

## Evidence

`StoreResolutionEndpointFilter.cs`, `StoreDirectoryResolver.cs`, `StoreRequestContextAccessor.cs`, `TenantClaimMatchEndpointFilter` في `MerchantEndpoints.cs`, `CustomerOrderEndpoints.cs`, وEF configurations.

## Consequences

الـ tenant boundary موزع بين route resolution وauthorization filters وservice queries وdatabase constraints. أي endpoint جديد يجب أن يطبق كل هذه المستويات المناسبة.

## Risks / Open questions

- إنشاء مراجعة منهجية لكل استعلام/كتابة للتأكد من StoreId filters.
- توثيق جميع العلاقات المركبة والمفاتيح الخارجية.
- اختبار PlatformAdmin cross-tenant operations بصورة منفصلة ومقصودة.
- الاتفاق على host/subdomain routing مستقبلًا إن أضيف.
