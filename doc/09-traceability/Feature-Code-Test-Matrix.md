# مصفوفة الربط بين القدرات والكود والاختبارات

> المعرفات `OBS-*` وصفية وليست متطلبات Stakeholder معتمدة.

| ID | Capability | API / Entry point | Service / Domain | Persistence | Test evidence |
|---|---|---|---|---|---|
| OBS-001 | Create store | `PlatformAdminEndpoints` | `PlatformStoreService`, `Store.Create` | Stores, StoreSettings | `PlatformStoreLifecycle_...` ضمن `PublicStoreCatalogIntegrationTests` |
| OBS-002 | Store lifecycle | approve/reject/suspend endpoints | `Store` methods, `PlatformStoreService` | Stores | `PlatformStoreLifecycle_...` |
| OBS-003 | Store resolution | Public/merchant/customer route groups | `StoreResolutionEndpointFilter`, `StoreDirectoryResolver` | Stores/Settings/Currencies | Unknown/non-active/concurrent store tests |
| OBS-004..008 | Public catalog | `PublicCatalogEndpoints` | `PublicCatalogQueryService` | Store, Category, Product, Images, Inventory | `PublicStoreCatalogIntegrationTests` |
| OBS-009 | Merchant store settings | `MerchantEndpoints` | `MerchantStoreService`, `StoreSettings` | Stores, Settings, DeliveryZones | `MerchantServices_...` |
| OBS-010 | Catalog management | `MerchantEndpoints` | `MerchantCatalogService`, Category/Product | Category/Product/Image/Inventory | `MerchantServices_...`, platform lifecycle tests |
| OBS-011 | Authentication | `AuthEndpoints` | `AuthService`, `PasswordHasherService` | AppUsers | `EndToEndCommerceAuthIntegrationTests` |
| OBS-012 | Session ticket | Cookie handler | `SqlSessionTicketStore` | SessionTickets | End-to-end auth tests; deeper tests exist in SessionTransportSpike only |
| OBS-013 | Cart | Customer cart endpoints | `CartService`, Cart/CartItem | Carts/CartItems | Full commerce integration scenario |
| OBS-014 | Checkout/idempotency | Customer `/checkout` | `CheckoutService`, `CheckoutRequest` | CheckoutRequests/Orders | Full commerce integration scenario; detailed concurrency cases must be reviewed |
| OBS-015 | Stock reservation | checkout/order operations | `InventoryItem`, `OrderService` | InventoryItems/Orders | Full commerce test covers named scenario; rowversion load stress not evidenced |
| OBS-016 | Customer orders | Customer order routes | `OrderService`, `Order` | Orders/Items/History | Full commerce integration scenario |
| OBS-017 | Merchant order actions | Merchant order routes | `OrderService`, `Order` | Orders/Items/History/Inventory | Full commerce integration scenario |
| OBS-018 | Health/OpenAPI | Host endpoints | `DatabaseReadinessHealthCheck` | SQL connection | `ProductHostIntegrationTests`, `PublicStoreCatalogIntegrationTests` |
| OBS-019 | Security controls | Host middleware | `ProductHostExtensions` | n/a | `OwaspApiSecurityIntegrationTests`, ProductHost tests |

## Coverage interpretation

- `Test evidence` refers to named tests that exist in the current tree, not a test run result.
- بعض الصفوف تربط feature باختبار end-to-end شامل، ولا يعني ذلك أن كل حالة فرعية مغطاة.
- لا يوجد في هذه المصفوفة ادعاء بأن كل source file قد روجع line-by-line.
- أي تغيير يكسر الربط يجب أن يحدّث هذه المصفوفة ضمن نفس PR.
