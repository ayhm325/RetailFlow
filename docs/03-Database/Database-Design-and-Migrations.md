# Database Design & Migrations

**Last Verified:** `2026-11-26` | **Branch:** `feature/public-store-catalog-read`

## 1. Current State (As-Is)
- **Database:** Dedicated SQL Server database `RetailFlowProduct` (`RetailFlowDbContext`), completely separate from `RetailFlowIdentitySpike`.
- **Migrations (`src/RetailFlow.Infrastructure/Persistence/Migrations/`):**
  1. `20261125123923_InitialCatalogSlice`: Creates `Currencies`, `Stores`, `StoreSettings`, `Categories`, `Products`, `ProductImages`, `InventoryItems`, and seeds 8 ISO 4217 currencies.
  2. `20261125130511_HardenCurrencyConstraints`: Adds `UX_Currencies_NumericCode` and updates `CK_Currencies_Code_Format` to enforce uppercase ASCII via `COLLATE Latin1_General_BIN2`.
  3. `20261125232250_AddStoreBrandingAddressesAndDeliveryZones`: Adds branding columns to `Stores` (`LogoUrl`, `PrimaryColor`, `ContactEmail`, `ContactPhone`), address columns to `StoreSettings` (`PublicAddress`, `FulfillmentAddress`), and creates `DeliveryZones` with tenant-scoped uniqueness and non-negative fee constraints.
  4. `20261125234743_AddCartOrdersAndCheckoutIdempotency` (exact timestamped filename in `Persistence/Migrations/`): Creates `Carts`, `CartItems`, `Orders`, `OrderItems`, `OrderStatusHistories`, and `CheckoutRequests` (`ADR-0005`, `ADR-0006`).
  5. `AddIdentityAndSessionTickets`: Creates `AppUsers` and `SessionTickets` (`AUTH-ADR-01`).

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
| `DeliveryZones` | `PK_DeliveryZones (Id)` | `UX_DeliveryZones_StoreId_NormalizedName (StoreId, NormalizedName)` | `FK_DeliveryZones_Stores_StoreId` | `CK_DeliveryZones_FlatFee_NonNegative` |
| `Carts` & `CartItems` | `PK_Carts`, `AK_Carts_StoreId_Id`, `PK_CartItems` | `UX_Carts_StoreId_CustomerId`, `UX_CartItems_StoreId_CartId_ProductId` | `FK_Carts_Stores_StoreId`, `FK_CartItems_Carts_StoreId_CartId` | `CK_CartItems_Quantity_Positive` |
| `Orders`, `OrderItems`, `OrderStatusHistories` | `PK_Orders`, `AK_Orders_StoreId_Id`, `PK_OrderItems`, `PK_OrderStatusHistories` | `UX_Orders_StoreId_OrderNumber` | `FK_Orders_Stores_StoreId`, `FK_Orders_Currencies_CurrencyCode`, `FK_OrderItems_Orders_StoreId_OrderId`, `FK_OrderStatusHistories_Orders_StoreId_OrderId` | `CK_Orders_OrderStatus`, `CK_Orders_FulfillmentMode`, `CK_Orders_FulfillmentStatus`, `CK_Orders_PaymentStatus`, `CK_Orders_Totals_NonNegative`, `CK_OrderItems_*` |
| `CheckoutRequests` | `PK_CheckoutRequests (Id)` | `UX_CheckoutRequests_StoreId_CustomerId_IdempotencyKey` | `FK_CheckoutRequests_Stores_StoreId` | `CK_CheckoutRequests_Status` |
| `AppUsers` & `SessionTickets` | `PK_AppUsers (Id)`, `PK_SessionTickets (Key)` | `UX_AppUsers_StoreId_NormalizedEmail`, `UX_AppUsers_Platform_NormalizedEmail` | — | `CK_AppUsers_Role` (`1,2,3`) |

## 3. Data Types & Seed Data
- **Money:** `Products.Price` and `DeliveryZones.FlatFee` use `decimal(19,4)` with `MoneyPrecision` domain validation by `Currency.MinorUnit` (no implicit rounding).
- **Timestamps:** `datetimeoffset(3)` (`CreatedAtUtc`).
- **Concurrency:** `InventoryItems.RowVersion` uses SQL Server `rowversion` (`IsRowVersion()`).
- **Currency Seed Data (`CurrencyConfiguration.cs`, SIX ISO 4217 List One reviewed `2026-11-25`):** `JOD` (`400`, `3`), `KWD` (`414`, `3`), `SAR` (`682`, `2`), `AED` (`784`, `2`), `USD` (`840`, `2`), `EUR` (`978`, `2`), `GBP` (`826`, `2`), `JPY` (`392`, `0`).

## 4. Target State (To-Be) & Gaps
- **To-Be:** Add `StoreMemberships`, `InventoryReservations`, `InventoryTransactions`, `Carts`, `CartItems`, `Orders`, `OrderItems`, `OrderStatusHistories`, `CheckoutRequests`, `DeliveryZoneAreas`, `GeoAreas`, and production Identity tables after `AUTH-ADR-01` acceptance.
- **Note on MARS:** `MultipleActiveResultSets=True` in local/CI connection strings disables EF Core transaction savepoints (emitting an EF Core warning in multi-operation transactions). Currently Product Host executes read-only `AsNoTracking()` queries without explicit savepoints.
