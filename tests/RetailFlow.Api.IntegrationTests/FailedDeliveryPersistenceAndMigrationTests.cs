using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class FailedDeliveryPersistenceAndMigrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private const string PreviousMigration = "20261126113905_AddSessionTicketAbsoluteLifetime";
    private const string TargetMigration = "20261128001900_AddFulfillmentAttemptItemConcurrencyToken";
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public FailedDeliveryPersistenceAndMigrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private string BuildTempDbConnectionString(string dbName)
    {
        var csb = new SqlConnectionStringBuilder(_fixture.ConnectionString)
        {
            InitialCatalog = dbName
        };
        return csb.ConnectionString;
    }

    private static RetailFlowDbContext CreateDbContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<RetailFlowDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new RetailFlowDbContext(options);
    }

    private async Task DropDatabaseIfExistsAsync(string dbName)
    {
        await using var conn = new SqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            IF DB_ID(N'{dbName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{dbName}];
            END
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<(Guid StoreId, Guid ProdId)> SeedLegacyStoreAndProductAsync(string connStr)
    {
        var storeId = Guid.NewGuid();
        var catId = Guid.NewGuid();
        var prodId = Guid.NewGuid();
        var invId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero);

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Currencies WHERE Code = 'JOD')
                INSERT INTO dbo.Currencies (Code, NumericCode, MinorUnit) VALUES ('JOD', '400', 3);

            INSERT INTO dbo.Stores (Id, Name, Slug, StoreType, Status, CreatedAtUtc)
            VALUES (@StoreId, N'Legacy Shop', @Slug, 1, 2, @T0);

            INSERT INTO dbo.StoreSettings (Id, StoreId, CurrencyCode, TimeZoneId, DeliveryEnabled, PickupEnabled)
            VALUES (@SettingsId, @StoreId, 'JOD', N'Asia/Amman', 1, 1);

            INSERT INTO dbo.Categories (Id, StoreId, Name, Slug, IsActive, SortOrder)
            VALUES (@CatId, @StoreId, N'Cat', N'cat', 1, 1);

            INSERT INTO dbo.Products (Id, StoreId, CategoryId, Name, Price, Status, CreatedAtUtc)
            VALUES (@ProdId, @StoreId, @CatId, N'Beans', 10.0000, 2, @T0);

            INSERT INTO dbo.InventoryItems (Id, StoreId, ProductId, OnHand, Reserved)
            VALUES (@InvId, @StoreId, @ProdId, 100, 0);
            """;
        cmd.Parameters.AddWithValue("@StoreId", storeId);
        cmd.Parameters.AddWithValue("@Slug", $"leg-{storeId.ToString("N")[..8]}");
        cmd.Parameters.AddWithValue("@SettingsId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@CatId", catId);
        cmd.Parameters.AddWithValue("@ProdId", prodId);
        cmd.Parameters.AddWithValue("@InvId", invId);
        cmd.Parameters.AddWithValue("@T0", t0);
        await cmd.ExecuteNonQueryAsync();
        return (storeId, prodId);
    }

    private static async Task<Guid> InsertLegacyOrderAsync(
        SqlConnection conn,
        Guid storeId,
        Guid prodId,
        int mode,
        int orderStatus,
        int fulfillmentStatus,
        int paymentStatus,
        int qty,
        params (int? FromOrder, int ToOrder, int? FromFul, int ToFul, DateTimeOffset Ts)[] history)
    {
        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var t0 = new DateTimeOffset(2026, 11, 20, 11, 0, 0, TimeSpan.Zero);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO dbo.Orders (
                Id, StoreId, CustomerId, OrderNumber, FulfillmentMode, OrderStatus, FulfillmentStatus, PaymentStatus,
                CurrencyCode, Subtotal, DeliveryFee, Total, CreatedAtUtc, ReservationExpiresAtUtc)
            VALUES (
                @OrderId, @StoreId, @CustId, @Num, @Mode, @OStatus, @FStatus, @PStatus,
                'JOD', 10.0000, 0.0000, 10.0000, @T0, @TExp);

            INSERT INTO dbo.OrderItems (
                Id, StoreId, OrderId, ProductId, ProductName, UnitPrice, Quantity, LineTotal)
            VALUES (
                @ItemId, @StoreId, @OrderId, @ProdId, N'Beans', 10.0000, @Qty, 10.0000 * @Qty);
            """;
        cmd.Parameters.AddWithValue("@OrderId", orderId);
        cmd.Parameters.AddWithValue("@StoreId", storeId);
        cmd.Parameters.AddWithValue("@CustId", Guid.NewGuid());
        cmd.Parameters.AddWithValue("@Num", $"LEG-{orderId.ToString("N")[..8]}");
        cmd.Parameters.AddWithValue("@Mode", mode);
        cmd.Parameters.AddWithValue("@OStatus", orderStatus);
        cmd.Parameters.AddWithValue("@FStatus", fulfillmentStatus);
        cmd.Parameters.AddWithValue("@PStatus", paymentStatus);
        cmd.Parameters.AddWithValue("@T0", t0);
        cmd.Parameters.AddWithValue("@TExp", t0.AddMinutes(15));
        cmd.Parameters.AddWithValue("@ItemId", itemId);
        cmd.Parameters.AddWithValue("@ProdId", prodId);
        cmd.Parameters.AddWithValue("@Qty", qty);
        await cmd.ExecuteNonQueryAsync();

        foreach (var (fo, to, ff, tf, ts) in history)
        {
            await using var hcmd = conn.CreateCommand();
            hcmd.CommandText = """
                INSERT INTO dbo.OrderStatusHistories (
                    Id, StoreId, OrderId, ActorRole, FromOrderStatus, ToOrderStatus,
                    FromFulfillmentStatus, ToFulfillmentStatus, OccurredAtUtc)
                VALUES (
                    @Id, @StoreId, @OrderId, N'MerchantOwner', @FO, @TO, @FF, @TF, @Ts);
                """;
            hcmd.Parameters.AddWithValue("@Id", Guid.NewGuid());
            hcmd.Parameters.AddWithValue("@StoreId", storeId);
            hcmd.Parameters.AddWithValue("@OrderId", orderId);
            hcmd.Parameters.AddWithValue("@FO", (object?)fo ?? DBNull.Value);
            hcmd.Parameters.AddWithValue("@TO", to);
            hcmd.Parameters.AddWithValue("@FF", (object?)ff ?? DBNull.Value);
            hcmd.Parameters.AddWithValue("@TF", tf);
            hcmd.Parameters.AddWithValue("@Ts", ts);
            await hcmd.ExecuteNonQueryAsync();
        }

        return orderId;
    }

    [Fact]
    public async Task ValidLegacyDataset_MigrationSucceeds_BackfillsAllStates_AndDownRollsBackCleanly()
    {
        var dbName = $"RetailFlowMigValid_{Guid.NewGuid():N}";
        var connStr = BuildTempDbConnectionString(dbName);

        try
        {
            await using (var db = CreateDbContext(connStr))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration);
            }

            var (storeId, prodId) = await SeedLegacyStoreAndProductAsync(connStr);
            var t = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

            Guid failedId;
            Guid cancelledPrepId;
            Guid pickupId;
            await using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();
                // 1. Pending Delivery (qty 1)
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 1, 1, 1, 1, (null, 1, null, 1, t));
                // 2. Confirmed Unfulfilled (qty 1)
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 1, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)));
                // 3. Confirmed Preparing (qty 1)
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 2, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)));
                // 4. Confirmed OutForDelivery (qty 1)
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 3, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 2, 2, 3, t.AddMinutes(3)));
                // 5. Confirmed Delivered (qty 1)
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 5, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 2, 2, 3, t.AddMinutes(3)), (2, 2, 3, 5, t.AddMinutes(4)));
                // 6. Completed Delivery
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 3, 5, 2, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 2, 2, 3, t.AddMinutes(3)), (2, 2, 3, 5, t.AddMinutes(4)), (2, 3, 5, 5, t.AddMinutes(5)));
                // 7. Confirmed Failed (qty 2)
                failedId = await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 7, 1, 2, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 2, 2, 3, t.AddMinutes(3)), (2, 2, 3, 7, t.AddMinutes(4)));
                // 8. Rejected
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 4, 8, 3, 1, (null, 1, null, 1, t), (1, 4, 1, 8, t.AddMinutes(1)));
                // 9. Cancelled before Preparing
                await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 5, 8, 3, 1, (null, 1, null, 1, t), (1, 5, 1, 8, t.AddMinutes(1)));
                // 10. Cancelled after Preparing (before dispatch)
                cancelledPrepId = await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 5, 8, 3, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 5, 2, 8, t.AddMinutes(3)));
                // 11. Pickup PickedUp (qty 1)
                pickupId = await InsertLegacyOrderAsync(conn, storeId, prodId, 2, 3, 6, 2, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 2, 2, 4, t.AddMinutes(3)), (2, 2, 4, 6, t.AddMinutes(4)), (2, 3, 6, 6, t.AddMinutes(5)));

                // Total reserved for Pending+Confirmed = 1+1+1+1+1+2 = 7
                await using var ucmd = conn.CreateCommand();
                ucmd.CommandText = "UPDATE dbo.InventoryItems SET Reserved = 7 WHERE StoreId = @StoreId AND ProductId = @ProdId;";
                ucmd.Parameters.AddWithValue("@StoreId", storeId);
                ucmd.Parameters.AddWithValue("@ProdId", prodId);
                await ucmd.ExecuteNonQueryAsync();
            }

            // Apply AUD-001 target migration
            await using (var db = CreateDbContext(connStr))
            {
                var migrator = db.GetService<IMigrator>();
                await migrator.MigrateAsync(TargetMigration);

                var failedAttempt = await db.FulfillmentAttempts.Include(a => a.Items).SingleAsync(a => a.OrderId == failedId);
                Assert.Equal(FulfillmentAttemptStatus.Failed, failedAttempt.Status);
                Assert.Equal(1, failedAttempt.AttemptNumber);
                Assert.Equal(DeliveryFailureReasonCode.LegacyUnknown, failedAttempt.FailureReasonCode);
                Assert.Equal(DeliveryFailureCategory.RequiresCorrection, failedAttempt.FailureCategory);
                Assert.Equal(t.AddMinutes(4), failedAttempt.FailedAtUtc);
                Assert.Single(failedAttempt.Items);
                Assert.Equal(ReturnDisposition.Pending, failedAttempt.Items.Single().ReturnDisposition);

                var failedOrderItem = await db.OrderItems.SingleAsync(i => i.OrderId == failedId);
                Assert.Equal(InventoryReservationStatus.ReturnPending, failedOrderItem.ReservationStatus);

                var abandonedAttempt = await db.FulfillmentAttempts.Include(a => a.Items).SingleAsync(a => a.OrderId == cancelledPrepId);
                Assert.Equal(FulfillmentAttemptStatus.Abandoned, abandonedAttempt.Status);
                Assert.Null(abandonedAttempt.AttemptNumber);
                Assert.Equal(ReturnDisposition.NotRequired, abandonedAttempt.Items.Single().ReturnDisposition);

                Assert.Empty(await db.FulfillmentAttempts.Where(a => a.OrderId == pickupId).ToListAsync());

                // Verify Down() rollback cleanly removes AUD-001 schema
                await migrator.MigrateAsync(PreviousMigration);
            }

            await using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT CASE WHEN OBJECT_ID('dbo.FulfillmentAttempts') IS NULL THEN 1 ELSE 0 END;";
                Assert.Equal(1, Convert.ToInt32(await checkCmd.ExecuteScalarAsync()));
            }
        }
        finally
        {
            await DropDatabaseIfExistsAsync(dbName);
        }
    }

    [Theory]
    [InlineData("MissingHistory")]
    [InlineData("EqualTimestamps")]
    [InlineData("BrokenChain")]
    [InlineData("InvalidTransition")]
    [InlineData("TerminalMismatch")]
    [InlineData("DispatchedCancelled")]
    [InlineData("AmbiguousCancelSource")]
    [InlineData("InvalidStateMatrix")]
    [InlineData("MissingInventoryItem")]
    [InlineData("MissingProduct")]
    [InlineData("ReservationMismatch")]
    [InlineData("ReservedExceedsOnHand")]
    public async Task BlockingLegacyData_ThrowsSql51000_AndLeavesSchemaAndDataUnchanged(string scenario)
    {
        var dbName = $"RetailFlowMigBlock_{Guid.NewGuid():N}";
        var connStr = BuildTempDbConnectionString(dbName);

        try
        {
            await using (var db = CreateDbContext(connStr))
            {
                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            var (storeId, prodId) = await SeedLegacyStoreAndProductAsync(connStr);
            var t = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

            await using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();
                switch (scenario)
                {
                    case "MissingHistory":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 1, 1, 1, 1);
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "EqualTimestamps":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 1, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t));
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "BrokenChain":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 2, 1, 1, (null, 1, null, 1, t), (2, 2, 1, 2, t.AddMinutes(1)));
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "InvalidTransition":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 3, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 3, t.AddMinutes(1)));
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "TerminalMismatch":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 2, 2, 1, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)));
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "DispatchedCancelled":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 5, 8, 3, 1, (null, 1, null, 1, t), (1, 2, 1, 1, t.AddMinutes(1)), (2, 2, 1, 2, t.AddMinutes(2)), (2, 2, 2, 3, t.AddMinutes(3)), (2, 5, 3, 8, t.AddMinutes(4)));
                        break;
                    case "AmbiguousCancelSource":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 5, 8, 3, 1, (null, 1, null, 1, t), (2, 5, 2, 8, t.AddMinutes(1)));
                        break;
                    case "InvalidStateMatrix":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 1, 2, 1, 1, (null, 1, null, 1, t), (1, 1, 1, 2, t.AddMinutes(1)));
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "MissingInventoryItem":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 1, 1, 1, 1, (null, 1, null, 1, t));
                        await using (var delInv = conn.CreateCommand())
                        {
                            delInv.CommandText = "DELETE FROM dbo.InventoryItems WHERE StoreId = @S AND ProductId = @P;";
                            delInv.Parameters.AddWithValue("@S", storeId);
                            delInv.Parameters.AddWithValue("@P", prodId);
                            await delInv.ExecuteNonQueryAsync();
                        }
                        break;
                    case "MissingProduct":
                        await InsertLegacyOrderAsync(conn, storeId, Guid.NewGuid(), 1, 1, 1, 1, 1, (null, 1, null, 1, t));
                        break;
                    case "ReservationMismatch":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 1, 1, 1, 2, (null, 1, null, 1, t));
                        await SetReservedAsync(conn, storeId, prodId, 1);
                        break;
                    case "ReservedExceedsOnHand":
                        await InsertLegacyOrderAsync(conn, storeId, prodId, 1, 1, 1, 1, 1, (null, 1, null, 1, t));
                        await using (var viol = conn.CreateCommand())
                        {
                            viol.CommandText = "ALTER TABLE dbo.InventoryItems NOCHECK CONSTRAINT CK_InventoryItems_Reserved_NotExceed_OnHand; UPDATE dbo.InventoryItems SET OnHand = 0, Reserved = 1 WHERE StoreId = @S;";
                            viol.Parameters.AddWithValue("@S", storeId);
                            await viol.ExecuteNonQueryAsync();
                        }
                        break;
                }
            }

            await using (var db = CreateDbContext(connStr))
            {
                var ex = await Assert.ThrowsAsync<SqlException>(() =>
                    db.GetService<IMigrator>().MigrateAsync(TargetMigration));
                Assert.Equal(51000, ex.Number);
                Assert.Contains("AUD-001 migration blocked", ex.Message, StringComparison.Ordinal);
            }

            // Verify zero schema changes and existing Orders intact
            await using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = """
                    SELECT
                        CASE WHEN OBJECT_ID('dbo.FulfillmentAttempts') IS NULL THEN 1 ELSE 0 END AS NoTable,
                        CASE WHEN COL_LENGTH('dbo.OrderItems', 'InventoryReservationStatus') IS NULL THEN 1 ELSE 0 END AS NoCol,
                        (SELECT COUNT(*) FROM dbo.Orders WHERE StoreId = @S) AS OrderCount;
                    """;
                checkCmd.Parameters.AddWithValue("@S", storeId);
                await using var reader = await checkCmd.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.Equal(1, reader.GetInt32(1));
                Assert.Equal(1, reader.GetInt32(2));
            }
        }
        finally
        {
            await DropDatabaseIfExistsAsync(dbName);
        }
    }

    private static async Task SetReservedAsync(SqlConnection conn, Guid storeId, Guid prodId, int reserved)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE dbo.InventoryItems SET Reserved = @R WHERE StoreId = @S AND ProductId = @P;";
        cmd.Parameters.AddWithValue("@R", reserved);
        cmd.Parameters.AddWithValue("@S", storeId);
        cmd.Parameters.AddWithValue("@P", prodId);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task OrderRootRowVersion_AdvancesOnEveryChildAttemptAndDispositionMutation()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "RV Shop", $"rv-{Guid.NewGuid():N}"[..12], StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var zone = DeliveryZone.Create(Guid.NewGuid(), store.Id, "Amman", 2.000m, settings, Jod, now, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Beans", 10.000m, settings, Jod, ProductStatus.Published, now);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, prod.Id, 10, 2);
        var orderId = Guid.NewGuid();
        var item = OrderItem.Create(Guid.NewGuid(), store.Id, orderId, prod.Id, "Beans", "BN-1", 10.000m, 2, Jod);
        var order = Order.Create(orderId, store, settings, Jod, Guid.NewGuid(), $"ORD-{orderId.ToString("N")[..8]}", FulfillmentMode.Delivery, [item], 2.000m, zone.Id, zone.Name, "St 1", now);
        order.Confirm(Guid.NewGuid(), "MerchantOwner", now.AddMinutes(1));

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.DeliveryZones.Add(zone);
            db.Categories.Add(cat);
            db.Products.Add(prod);
            db.InventoryItems.Add(inv);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        try
        {
            byte[] rvInit, rvPrep, rvDisp, rvFail, rvDispRes, rvCorr, rvRetry;
            Guid attemptId;

            // 1. Preparing
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvInit = loaded.RowVersion.ToArray();
                loaded.AdvanceFulfillment(Guid.NewGuid(), "MerchantOwner", FulfillmentStatus.Preparing, now.AddMinutes(2));
                await db.SaveChangesAsync();
            }

            // 2. OutForDelivery
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvPrep = loaded.RowVersion.ToArray();
                Assert.False(rvPrep.AsSpan().SequenceEqual(rvInit));
                loaded.AdvanceFulfillment(Guid.NewGuid(), "MerchantOwner", FulfillmentStatus.OutForDelivery, now.AddMinutes(3));
                await db.SaveChangesAsync();
            }

            // 3. Failure
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvDisp = loaded.RowVersion.ToArray();
                Assert.False(rvDisp.AsSpan().SequenceEqual(rvPrep));
                loaded.RecordDeliveryFailure(Guid.NewGuid(), "MerchantOwner", DeliveryFailureReasonCode.IncompleteAddress, "Bad addr", now.AddMinutes(4));
                await db.SaveChangesAsync();
            }

            // 4. Disposition
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvFail = loaded.RowVersion.ToArray();
                Assert.False(rvFail.AsSpan().SequenceEqual(rvDisp));
                attemptId = loaded.FulfillmentAttempts.Single().Id;
                loaded.ResolveInventoryDisposition(Guid.NewGuid(), "MerchantOwner", attemptId, [(item.Id, ReturnDisposition.Restockable, "Back")], now.AddMinutes(5));
                await db.SaveChangesAsync();
            }

            // 5. Correction (plus rejected Retry before correction does NOT advance RowVersion)
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvDispRes = loaded.RowVersion.ToArray();
                Assert.False(rvDispRes.AsSpan().SequenceEqual(rvFail));
                Assert.Throws<OrderStateConflictException>(() => loaded.RetryDelivery(Guid.NewGuid(), "MerchantOwner", now.AddMinutes(5.5), "blocked", "h0"));
                Assert.True(loaded.RowVersion.AsSpan().SequenceEqual(rvDispRes));
                loaded.ResolveDeliveryCorrection(Guid.NewGuid(), "MerchantOwner", "Updated address", now.AddMinutes(6));
                await db.SaveChangesAsync();
            }

            // 6. Retry
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvCorr = loaded.RowVersion.ToArray();
                Assert.False(rvCorr.AsSpan().SequenceEqual(rvDispRes));
                loaded.RetryDelivery(Guid.NewGuid(), "MerchantOwner", now.AddMinutes(7), "retry-rv", "hash-rv");
                await db.SaveChangesAsync();
            }

            // 7. Cancellation
            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.Include(o => o.Items).Include(o => o.FulfillmentAttempts).ThenInclude(a => a.Items).SingleAsync(o => o.Id == orderId);
                rvRetry = loaded.RowVersion.ToArray();
                Assert.False(rvRetry.AsSpan().SequenceEqual(rvCorr));
                loaded.CancelByMerchantOrTimeout(Guid.NewGuid(), "MerchantOwner", "Cancel", now.AddMinutes(8));
                await db.SaveChangesAsync();
            }

            await using (var db = _fixture.CreateDbContext())
            {
                var loaded = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
                Assert.False(loaded.RowVersion.AsSpan().SequenceEqual(rvRetry));
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task SqlConstraints_UniqueIndexes_CheckConstraints_AndAtomicity_Enforced()
    {
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Con Shop", $"con-{Guid.NewGuid():N}"[..12], StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var zone = DeliveryZone.Create(Guid.NewGuid(), store.Id, "Amman", 2.000m, settings, Jod, now, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Beans", 10.000m, settings, Jod, ProductStatus.Published, now);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, prod.Id, 10, 2);
        var orderId = Guid.NewGuid();
        var item = OrderItem.Create(Guid.NewGuid(), store.Id, orderId, prod.Id, "Beans", "BN-1", 10.000m, 2, Jod);
        var order = Order.Create(orderId, store, settings, Jod, Guid.NewGuid(), $"ORD-{orderId.ToString("N")[..8]}", FulfillmentMode.Delivery, [item], 2.000m, zone.Id, zone.Name, "St 1", now);
        order.Confirm(Guid.NewGuid(), "MerchantOwner", now.AddMinutes(1));
        order.AdvanceFulfillment(Guid.NewGuid(), "MerchantOwner", FulfillmentStatus.Preparing, now.AddMinutes(2));
        order.AdvanceFulfillment(Guid.NewGuid(), "MerchantOwner", FulfillmentStatus.OutForDelivery, now.AddMinutes(3));
        order.RecordDeliveryFailure(Guid.NewGuid(), "MerchantOwner", DeliveryFailureReasonCode.CustomerUnavailable, null, now.AddMinutes(4));
        var attemptId = order.FulfillmentAttempts.Single().Id;
        var attemptItemId = order.FulfillmentAttempts.Single().Items.Single().Id;
        order.CancelByMerchantOrTimeout(Guid.NewGuid(), "MerchantOwner", "Cancel", now.AddMinutes(5));
        order.ResolveInventoryDisposition(Guid.NewGuid(), "MerchantOwner", attemptId, [(item.Id, ReturnDisposition.Damaged, "Damaged")], now.AddMinutes(6));

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.DeliveryZones.Add(zone);
            db.Categories.Add(cat);
            db.Products.Add(prod);
            db.InventoryItems.Add(inv);
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        try
        {
            // 1. Atomic WriteOff + InventoryAdjustment rollback on stale InventoryItem RowVersion
            await using (var staleDb = _fixture.CreateDbContext())
            await using (var freshDb = _fixture.CreateDbContext())
            {
                var staleInv = await staleDb.InventoryItems.SingleAsync(i => i.ProductId == prod.Id);
                var freshInv = await freshDb.InventoryItems.SingleAsync(i => i.ProductId == prod.Id);
                freshInv.SetOnHand(11);
                await freshDb.SaveChangesAsync();

                staleInv.WriteOffReservation(2);
                staleDb.InventoryAdjustments.Add(InventoryAdjustment.CreateWriteOff(
                    Guid.NewGuid(), store.Id, prod.Id, orderId, item.Id, attemptItemId,
                    InventoryAdjustmentOperationType.WriteOffDamaged, 2, DeliveryFailureReasonCode.CustomerUnavailable,
                    Guid.NewGuid(), now.AddMinutes(7), "Damaged"));

                await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
            }

            await using (var verifyDb = _fixture.CreateDbContext())
            {
                Assert.Empty(await verifyDb.InventoryAdjustments.Where(a => a.StoreId == store.Id).ToListAsync());
                var currentInv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
                Assert.Equal(11, currentInv.OnHand);
                Assert.Equal(2, currentInv.Reserved);
            }

            // 2. Duplicate InventoryAdjustment WriteOff rejected by UX_InventoryAdjustments_OrderItem_WriteOff
            await using (var db = _fixture.CreateDbContext())
            {
                db.InventoryAdjustments.Add(InventoryAdjustment.CreateWriteOff(
                    Guid.NewGuid(), store.Id, prod.Id, orderId, item.Id, attemptItemId,
                    InventoryAdjustmentOperationType.WriteOffDamaged, 2, DeliveryFailureReasonCode.CustomerUnavailable,
                    Guid.NewGuid(), now.AddMinutes(8), "First"));
                await db.SaveChangesAsync();

                db.InventoryAdjustments.Add(InventoryAdjustment.CreateWriteOff(
                    Guid.NewGuid(), store.Id, prod.Id, orderId, item.Id, attemptItemId,
                    InventoryAdjustmentOperationType.WriteOffLost, 2, DeliveryFailureReasonCode.LostInTransit,
                    Guid.NewGuid(), now.AddMinutes(9), "Duplicate"));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // 3. Direct SQL checks for Filtered Unique Indexes & Check Constraints
            await using var conn = new SqlConnection(_fixture.ConnectionString);
            await conn.OpenAsync();

            // Duplicate AttemptNumber = 1 rejected
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttempts (Id, StoreId, OrderId, AttemptNumber, Status, IsActive, PreparedAtUtc)
                VALUES ('{Guid.NewGuid()}', '{store.Id}', '{orderId}', 1, 4, 0, SYSUTCDATETIME());
                """, 2601);

            // Check constraint CK_FulfillmentAttempts_IsActive_Status rejects IsActive=1 with Failed(4)
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttempts (Id, StoreId, OrderId, AttemptNumber, Status, IsActive, PreparedAtUtc)
                VALUES ('{Guid.NewGuid()}', '{store.Id}', '{orderId}', 2, 4, 1, SYSUTCDATETIME());
                """, 547);

            // Insert a valid Prepared attempt (IsActive = 1) with no items to test Active uniqueness and AttemptItem check constraints
            var activeAttemptId = Guid.NewGuid();
            await using (var prepCmd = conn.CreateCommand())
            {
                prepCmd.CommandText = $"""
                    INSERT INTO dbo.FulfillmentAttempts (Id, StoreId, OrderId, AttemptNumber, Status, IsActive, PreparedAtUtc, RetryIdempotencyKey)
                    VALUES ('{activeAttemptId}', '{store.Id}', '{orderId}', NULL, 1, 1, SYSUTCDATETIME(), 'retry-key-1');
                    """;
                await prepCmd.ExecuteNonQueryAsync();
            }

            // Duplicate active attempt rejected by UX_FulfillmentAttempts_StoreId_OrderId_Active
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttempts (Id, StoreId, OrderId, AttemptNumber, Status, IsActive, PreparedAtUtc)
                VALUES ('{Guid.NewGuid()}', '{store.Id}', '{orderId}', NULL, 1, 1, SYSUTCDATETIME());
                """, 2601);

            // Duplicate RetryIdempotencyKey rejected by UX_FulfillmentAttempts_StoreId_OrderId_RetryIdempotencyKey
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttempts (Id, StoreId, OrderId, AttemptNumber, Status, IsActive, PreparedAtUtc, RetryIdempotencyKey)
                VALUES ('{Guid.NewGuid()}', '{store.Id}', '{orderId}', 2, 4, 0, SYSUTCDATETIME(), 'retry-key-1');
                """, 2601);

            // Check constraint CK_FulfillmentAttemptItems_ReturnedAtUtc_Semantics rejects Lost(4) with ReturnedAtUtc
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttemptItems (Id, StoreId, OrderId, FulfillmentAttemptId, OrderItemId, ProductId, Quantity, ReturnDisposition, ReturnedAtUtc)
                VALUES ('{Guid.NewGuid()}', '{store.Id}', '{orderId}', '{activeAttemptId}', '{item.Id}', '{prod.Id}', 2, 4, SYSUTCDATETIME());
                """, 547);

            // Check constraint CK_FulfillmentAttemptItems_ReturnedAtUtc_Semantics rejects Restockable(2) with NULL ReturnedAtUtc
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttemptItems (Id, StoreId, OrderId, FulfillmentAttemptId, OrderItemId, ProductId, Quantity, ReturnDisposition, ReturnedAtUtc)
                VALUES ('{Guid.NewGuid()}', '{store.Id}', '{orderId}', '{activeAttemptId}', '{item.Id}', '{prod.Id}', 2, 2, NULL);
                """, 547);

            // Cross-store composite FK rejected
            await AssertSqlErrorAsync(conn, $"""
                INSERT INTO dbo.FulfillmentAttemptItems (Id, StoreId, OrderId, FulfillmentAttemptId, OrderItemId, ProductId, Quantity, ReturnDisposition)
                VALUES ('{Guid.NewGuid()}', '{Guid.NewGuid()}', '{orderId}', '{attemptId}', '{item.Id}', '{prod.Id}', 2, 0);
                """, 547);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    private static async Task AssertSqlErrorAsync(SqlConnection conn, string sql, int expectedNumber)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var ex = await Assert.ThrowsAsync<SqlException>(() => cmd.ExecuteNonQueryAsync());
        Assert.Equal(expectedNumber, ex.Number);
    }
}
