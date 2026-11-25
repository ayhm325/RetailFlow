# Requirements & Business Rules

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`
**Gate 1 Status:** Canonical registers established for `FR-001`–`FR-042`, `NFR-001`–`NFR-009`, `BR-001`–`BR-012`, `AC-01`–`AC-08`, and `UC-01`–`UC-10`.

## Canonical Gate 1 Registers & Specifications (Single Source of Truth)

| Artifact | Purpose | Path |
|---|---|---|
| **Functional Requirements Matrix** | Complete `FR-001`–`FR-042` lifecycle, implementation, verification & CI evidence | [Requirements-Traceability-Matrix.csv](../09-Audit-and-Traceability/Requirements-Traceability-Matrix.csv) |
| **Non-Functional Requirements Register** | Complete `NFR-001`–`NFR-009` targets, owners, and verification plans | [NFR-Register.csv](NFR-Register.csv) |
| **Business Rules Traceability Matrix** | Complete `BR-001`–`BR-012` mapped to invariants, aggregates & tests | [Business-Rules-Traceability-Matrix.csv](Business-Rules-Traceability-Matrix.csv) |
| **Acceptance Criteria Register** | Canonical `AC-01`–`AC-08` acceptance criteria | [Acceptance-Criteria.csv](Acceptance-Criteria.csv) |
| **AC Reconciliation Report** | Resolution of `AC-07` conflict and external `AC-08`–`AC-10` status | [Acceptance-Criteria-Reconciliation.md](Acceptance-Criteria-Reconciliation.md) |
| **Use Case Specifications** | Complete `UC-01`–`UC-10` specifications, Gherkin examples & cross-cutting rules | [Use-Case-Specifications.md](Use-Case-Specifications.md) |
| **Change Log & Open Decisions** | Gate 1 Change Requests (`CR-G1-*`) and Open Decisions (`OD-001`–`OD-008`) | [Change-Log-and-Open-Decisions.md](../08-Engineering-Management/Change-Log-and-Open-Decisions.md) |

## 1. Implemented Functional Requirements (As-Is Snapshot `04392c8`)

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
