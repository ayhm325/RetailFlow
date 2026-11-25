# Requirements & Business Rules

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Implemented Functional Requirements (As-Is)

| ID | Requirement | Implementation Evidence | Status |
|---|---|---|---|
| **FR-003** | Unique `StoreSlug` per store | `Store.NormalizeSlug`, `UX_Stores_Slug` (`StoreConfiguration.cs`) | Implemented and verified |
| **FR-005** | Support `Physical` and `OnlineOnly` store types | `StoreType` enum, `CK_Stores_StoreType` | Implemented and verified |
| **FR-010 / FR-011** | Simple products (`Name`, `Description`, `Price`, optional `Sku`, `Draft`/`Published`/`Archived`) | `Product.cs`, `ProductStatus.cs`, `ProductConfiguration.cs` | Implemented and verified |
| **FR-012 / FR-019** | Hide `Draft`/`Archived` products and inactive stores/categories from public catalog | `StoreDirectoryResolver.cs`, `PublicCatalogQueryService.cs` | Implemented and verified |
| **FR-020 / FR-021** | Search, filter (`categoryId`/`categorySlug`), sort, paginate, and view product details & availability | `PublicCatalogEndpoints.cs`, `PublicCatalogQueryService.cs` | Implemented and verified |

## 2. Implemented Business Rules & Domain Invariants

- **BR-001 / ST-01:** `Store.Slug` is normalized (`Trim().ToLowerInvariant()`) and globally unique (`UX_Stores_Slug`).
- **BR-002 / REL-01:** Tenant-owned entities (`Category`, `Product`, `ProductImage`, `InventoryItem`, `StoreSettings`) carry `StoreId` and are bound by SQL composite FKs `(StoreId, CategoryId)` and `(StoreId, ProductId)` (`ON DELETE NO ACTION`).
- **ST-02:** Only `StoreStatus.Active` stores resolve in `StoreDirectoryResolver`; `PendingApproval`, `Suspended`, and `Rejected` return `404 ProblemDetails`.
- **ST-03:** `StoreSettings.Create` and `UpdateFulfillmentOptions` reject `pickupEnabled == true` when `Store.StoreType == StoreType.OnlineOnly`.
- **PR-02:** Only `ProductStatus.Published` products in `Category.IsActive == true` categories are exposed in public catalog list and detail endpoints.
- **PR-03:** `Product.Sku` is optional and unique per store (`UX_Products_StoreId_Sku WHERE [Sku] IS NOT NULL`).
- **IN-01 / IN-02:** `OnHand >= 0`, `Reserved >= 0`, `Reserved <= OnHand` enforced in both `InventoryItem` and SQL `CHECK` constraints.
- **MNY-01:** `Product.Create` enforces `StoreSettings.StoreId == storeId`, `StoreSettings.CurrencyCode == currency.Code`, and `MoneyPrecision.EnsureValidAmount(price, currency.MinorUnit)` without implicit rounding; SQL stores `decimal(19,4)`.

## 3. Planned / Missing Requirements (To-Be)
- Merchant/Admin write APIs (`FR-001..009`, `FR-013`, `FR-032..038`), Customer Identity (`FR-016..018` — currently in `IdentitySpike` PoC), Cart & Checkout (`FR-023..031`), and Delivery Zones (`FR-038..042`) are not yet implemented in `src/`.
- See [Requirements-Traceability-Matrix.csv](../09-Audit-and-Traceability/Requirements-Traceability-Matrix.csv) for full mapping.
