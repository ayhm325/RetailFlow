# Database Design & Migrations

**Last Verified:** `2026-11-26` | **Verified Commit:** `04392c875c275e99f4b5ee378f63b3a8b8fc541d`

## 1. Current State (As-Is)
- **Database:** Dedicated SQL Server database `RetailFlowProduct` (`RetailFlowDbContext`), completely separate from `RetailFlowIdentitySpike`.
- **Migrations (`src/RetailFlow.Infrastructure/Persistence/Migrations/`):**
  1. `20261125123923_InitialCatalogSlice`: Creates `Currencies`, `Stores`, `StoreSettings`, `Categories`, `Products`, `ProductImages`, `InventoryItems`, and seeds 8 ISO 4217 currencies.
  2. `20261125130511_HardenCurrencyConstraints`: Adds `UX_Currencies_NumericCode` and updates `CK_Currencies_Code_Format` to enforce uppercase ASCII via `COLLATE Latin1_General_BIN2`.

## 2. Tables, Keys & Constraints

| Table | Primary Key | Unique Indexes | Foreign Keys (`Restrict` / `NO ACTION`) | Check Constraints |
|---|---|---|---|---|
| `Currencies` | `PK_Currencies (Code)` | `UX_Currencies_NumericCode (NumericCode)` | — | `CK_Currencies_Code_Format`, `CK_Currencies_NumericCode_Format`, `CK_Currencies_MinorUnit_Range` (`0..4`) |
| `Stores` | `PK_Stores (Id)` | `UX_Stores_Slug (Slug)` | — | `CK_Stores_StoreType` (`1,2`), `CK_Stores_Status` (`1,2,3,4`) |
| `StoreSettings` | `PK_StoreSettings (Id)` | `UX_StoreSettings_StoreId (StoreId)` | `FK_StoreSettings_Stores_StoreId`, `FK_StoreSettings_Currencies_CurrencyCode` | — |
| `Categories` | `PK_Categories (Id)` + `AK_Categories_StoreId_Id` | `UX_Categories_StoreId_Slug (StoreId, Slug)` | `FK_Categories_Stores_StoreId` | — |
| `Products` | `PK_Products (Id)` + `AK_Products_StoreId_Id` | `UX_Products_StoreId_Sku (StoreId, Sku) WHERE [Sku] IS NOT NULL` | `FK_Products_Stores_StoreId`, `FK_Products_Categories_StoreId_CategoryId` | `CK_Products_Price_NonNegative`, `CK_Products_Status` (`1,2,3`) |
| `ProductImages` | `PK_ProductImages (Id)` | — | `FK_ProductImages_Products_StoreId_ProductId` | — |
| `InventoryItems` | `PK_InventoryItems (Id)` | `UX_InventoryItems_StoreId_ProductId (StoreId, ProductId)` | `FK_InventoryItems_Products_StoreId_ProductId` | `CK_InventoryItems_OnHand_NonNegative`, `CK_InventoryItems_Reserved_NonNegative`, `CK_InventoryItems_Reserved_NotExceed_OnHand` |

## 3. Data Types & Seed Data
- **Money:** `Products.Price` uses `decimal(19,4)` with `MoneyPrecision` domain validation by `Currency.MinorUnit` (no implicit rounding).
- **Timestamps:** `datetimeoffset(3)` (`CreatedAtUtc`).
- **Concurrency:** `InventoryItems.RowVersion` uses SQL Server `rowversion` (`IsRowVersion()`).
- **Currency Seed Data (`CurrencyConfiguration.cs`, SIX ISO 4217 List One reviewed `2026-11-25`):** `JOD` (`400`, `3`), `KWD` (`414`, `3`), `SAR` (`682`, `2`), `AED` (`784`, `2`), `USD` (`840`, `2`), `EUR` (`978`, `2`), `GBP` (`826`, `2`), `JPY` (`392`, `0`).

## 4. Target State (To-Be) & Gaps
- **To-Be:** Add `StoreMemberships`, `StoreBrandings`, `StoreAddresses`, `InventoryReservations`, `InventoryTransactions`, `Carts`, `CartItems`, `Orders`, `OrderItems`, `OrderStatusHistories`, `CheckoutRequests`, `DeliveryZones`, `DeliveryZoneAreas`, `GeoAreas`, and production Identity tables after `AUTH-ADR-01` acceptance.
- **Note on MARS:** `MultipleActiveResultSets=True` in local/CI connection strings disables EF Core transaction savepoints (emitting an EF Core warning in multi-operation transactions). Currently Product Host executes read-only `AsNoTracking()` queries without explicit savepoints.
