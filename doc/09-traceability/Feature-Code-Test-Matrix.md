# مصفوفة الربط بين القدرات والكود والاختبارات

> المعرفات `OBS-*` وصفية وليست متطلبات Stakeholder معتمدة.

| ID | Capability | API / Entry point | Service / Domain | Persistence | Test evidence |
|---|---|---|---|---|---|
| OBS-001 | Create store | `PlatformAdminEndpoints` | `PlatformStoreService`, `Store.Create` | Stores, StoreSettings | `PlatformStoreLifecycle_...` ضمن `PublicStoreCatalogIntegrationTests` |
| OBS-002 | Store lifecycle | approve/reject/suspend endpoints | `Store` methods, `PlatformStoreService` | Stores | `PlatformStoreLifecycle_...` |
| OBS-003 | Store resolution | Public/merchant/customer route groups | `StoreResolutionEndpointFilter`, `StoreDirectoryResolver` | Stores/Settings/Currencies | Unknown/non-active/concurrent store tests |
| OBS-004..008 | Public catalog | `PublicCatalogEndpoints` | `PublicCatalogQueryService` | Store, Category, Product, Images, Inventory | `PublicStoreCatalogIntegrationTests` |
| OBS-009 | Merchant store settings | `MerchantEndpoints` | `MerchantStoreService`, `StoreSettings` | Stores, Settings, DeliveryZones | `MerchantServices_...` |
| OBS-010 | Catalog management | `MerchantEndpoints` | `MerchantCatalogService`, Category/Product, `InventoryItem`, `InventoryOnHandBelowReservedException`, `InventoryConcurrencyConflictException` | Category/Product/Image/Inventory | `MerchantServices_...`, platform lifecycle tests, `InventoryAdjustmentTests`, `OrderDomainTests`, `DomainInvariantsTests` |
| OBS-011 | Authentication & MFA | `AuthEndpoints` | `AuthService`, `RetailFlowPasswordHasher`, `TotpCryptoHelper` | `AspNetUsers`, `AspNetRoles`, `AspNetUserTokens` | `EndToEndCommerceAuthIntegrationTests`, `IdentitySecurityHardeningTests`, `PlatformAdminMfaHardeningTests`, `PasswordHasherTests`, `PasswordPolicyHardeningTests` |
| OBS-012 | Session ticket & DP | Cookie handler, `DataProtectionSetup` | `SqlSessionTicketStore`, `RetailFlowCookieEvents` | `SessionTickets`, `DataProtectionKeys` | `SessionLifetimeHardeningTests`, `DataProtectionHardeningTests`, `IdentityAdoptionIntegrationTests` |
| OBS-013 | Cart | Customer cart endpoints | `CartService`, `CartProductUnavailableException`, Cart/CartItem | Carts/CartItems | `CartConcurrencyTests`, `CartAndOrderValidationTests`, `EndToEndCommerceAuthIntegrationTests` |
| OBS-014 | Checkout/idempotency | Customer `/checkout` | `CheckoutService`, `CheckoutRequest` | CheckoutRequests/Orders | `CheckoutErrorClassificationTests`, `CheckoutLeaseAndCookieScopeTests`, `AuditPhasesIntegrationTests` |
| OBS-015 | Stock reservation & maintenance | checkout/order + `MaintenanceBackgroundService` | `InventoryItem`, `OrderService` | InventoryItems/Orders | `MaintenanceBatchConcurrencyTests`, `EndToEndCommerceAuthIntegrationTests` |
| OBS-016 | Customer orders | Customer order routes | `OrderService`, `Order`, `OrderStateConflictException`, `OrderConcurrencyConflictException`, `RetailFlowExceptionHandler` | Orders/Items/History | `EndToEndCommerceAuthIntegrationTests`, `OrderLifecycleConflictTests`, `OrderDomainTests` |
| OBS-017 | Merchant order actions | Merchant order routes | `OrderService`, `Order`, `OrderStateConflictException`, `OrderConcurrencyConflictException`, `RetailFlowExceptionHandler` | Orders/Items/History/Inventory | `EndToEndCommerceAuthIntegrationTests`, `OrderLifecycleConflictTests`, `CartAndOrderValidationTests`, `OrderDomainTests` |
| OBS-018 | Health/OpenAPI | Host endpoints | `DatabaseReadinessHealthCheck` | SQL connection | `ProductHostIntegrationTests`, `PublicStoreCatalogIntegrationTests` |
| OBS-019 | Security controls | Host middleware | `ProductHostExtensions` | n/a | `OwaspApiSecurityIntegrationTests`, ProductHost tests |

## Coverage interpretation

- `Test evidence` refers to named tests that exist in the current tree, not a test run result.
- بعض الصفوف تربط feature باختبار end-to-end شامل، ولا يعني ذلك أن كل حالة فرعية مغطاة.
- لا يوجد في هذه المصفوفة ادعاء بأن كل source file قد روجع line-by-line.
- أي تغيير يكسر الربط يجب أن يحدّث هذه المصفوفة ضمن نفس PR.
