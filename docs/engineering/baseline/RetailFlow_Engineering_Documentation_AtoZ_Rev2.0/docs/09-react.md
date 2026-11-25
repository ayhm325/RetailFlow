# 09. معمارية React وتجربة المستخدم

## 09.1 حالة التنفيذ

المخطط الأصلي يحدد React + TypeScript. لكن baseline لا يحتوي `ClientApp` أو SPA فعلية. كل ما في هذا الفصل Target Architecture وليس وصفاً لواجهة موجودة.

## 09.2 أهداف الواجهة

- Storefront مستقل لكل Store slug.
- Merchant Back Office منفصل وظيفياً.
- Responsive mobile/desktop.
- مكونات مشتركة دون مشاركة بيانات أو cache بين المتاجر.
- Accessibility وKeyboard navigation وحقول وأخطاء مفهومة.

## 09.3 تقسيم Feature-based مقترح

```text
ClientApp/src/
  app/                 # routing, providers, shell
  storefront/          # public store pages
  backoffice/          # merchant workspace
  features/
    auth/ catalog/ cart/ checkout/ orders/
    inventory/ store-settings/ fulfillment/
  shared/
    api/ components/ forms/ hooks/ utils/
  styles/
```

لا تُنشئ مجلدات فارغة أو طبقات لمجرد التنظيم؛ يجب أن تخدم حدوداً قابلة للصيانة.

## 09.4 Tenant Context في React

- استخرج slug من URL لاستخدامه في API request فقط.
- لا تعتبر slug أو StoreId في الواجهة دليلاً على الصلاحية.
- الخادم يعيد StoreContext موثوقاً.
- cache keys تشمل tenant scope، وعند تبديل المتجر امسح cache المرتبط بالمتجر السابق.
- لا تخزن صلاحيات أو session tokens في localStorage/sessionStorage.

## 09.5 Cookie/CSRF integration

إذا اعتمدت Cookie sessions:
- استخدم same-origin credentials.
- أضف `X-RetailFlow-Request: 1` للطلبات المغيّرة للحالة وفق قرار CSRF.
- 401 يعالج حالة انتهاء/غياب الجلسة دون loop.
- 403 يعرض منعاً واضحاً ولا يحاول client تجاوز Authorization.
- لا تُنشئ client-side source of truth لحالة AccountKind/StoreId.

## 09.6 واجهات المستخدم

Storefront: home/catalog/search/category/details. Customer: register/login/profile/address/cart/checkout/orders. Merchant: dashboard/products/categories/inventory/orders/fulfillment/settings. Platform Admin: store request review/status.

## 09.7 UI Quality Gates

Loading/empty/error لكل صفحة؛ validation متوافق مع backend؛ duplicate checkout protection؛ accessibility labels/focus/contrast؛ responsive checks؛ keyboard flows. الواجهة لا تحمي pricing أو stock أو ownership أو state transitions.
