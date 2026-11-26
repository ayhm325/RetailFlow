using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class MaintenanceBatchConcurrencyTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public MaintenanceBatchConcurrencyTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class DelegateOrderReleaseHook : IOrderReleaseHook
    {
        private readonly Func<Guid, int, CancellationToken, Task> _callback;

        public DelegateOrderReleaseHook(Func<Guid, int, CancellationToken, Task> callback) =>
            _callback = callback;

        public Task OnBeforeSaveOrderReleaseAsync(Guid orderId, int attempt, CancellationToken cancellationToken) =>
            _callback(orderId, attempt, cancellationToken);
    }

    private async Task<(Store Store, Product ProductA, Product ProductB, Order OrderA, Order OrderB)> SeedTwoExpiredOrdersAsync(
        int qtyA = 2,
        int qtyB = 3,
        int extraReservedA = 0)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        // Created 5m ago -> ReservationExpiresAtUtc is +10m in the future, so parallel MaintenanceBackgroundService
        // instances using TimeProvider.System never steal these test orders; tests sweep with +20m.
        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-5);
        var store = Store.Create(Guid.NewGuid(), "Maint Shop", $"maint-{suffix}", StoreType.Physical, StoreStatus.Active, oldTime);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prodA = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Prod A", 5.000m, settings, Jod, ProductStatus.Published, oldTime);
        var prodB = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Prod B", 7.000m, settings, Jod, ProductStatus.Published, oldTime);
        var invA = InventoryItem.Create(Guid.NewGuid(), store.Id, prodA.Id, onHand: 20, reserved: qtyA + extraReservedA);
        var invB = InventoryItem.Create(Guid.NewGuid(), store.Id, prodB.Id, onHand: 20, reserved: qtyB);
        var custId = Guid.NewGuid();

        var orderAId = Guid.NewGuid();
        var orderA = Order.Create(
            orderAId, store, settings, Jod, custId, $"ORDA-{suffix}", FulfillmentMode.Pickup,
            [OrderItem.Create(Guid.NewGuid(), store.Id, orderAId, prodA.Id, prodA.Name, prodA.Sku, prodA.Price, qtyA, Jod)],
            0m, null, null, null, oldTime);

        var orderBId = Guid.NewGuid();
        var orderB = Order.Create(
            orderBId, store, settings, Jod, custId, $"ORDB-{suffix}", FulfillmentMode.Pickup,
            [OrderItem.Create(Guid.NewGuid(), store.Id, orderBId, prodB.Id, prodB.Name, prodB.Sku, prodB.Price, qtyB, Jod)],
            0m, null, null, null, oldTime.AddSeconds(5));

        await using var db = _fixture.CreateDbContext();
        db.Stores.Add(store);
        db.StoreSettings.Add(settings);
        db.Categories.Add(cat);
        db.Products.AddRange(prodA, prodB);
        db.InventoryItems.AddRange(invA, invB);
        db.Orders.AddRange(orderA, orderB);
        await db.SaveChangesAsync();

        return (store, prodA, prodB, orderA, orderB);
    }

    [Fact]
    public async Task TestA_ConflictOnFirstOrder_DoesNotCorruptRemainingBatch()
    {
        var (store, prodA, prodB, orderA, orderB) = await SeedTwoExpiredOrdersAsync();
        try
        {
            var hook = new DelegateOrderReleaseHook(async (orderId, attempt, ct) =>
            {
                if (orderId == orderA.Id && attempt == 1)
                {
                    // Force a real RowVersion conflict on Order A's InventoryItem during attempt 1
                    await using var competingDb = _fixture.CreateDbContext();
                    await competingDb.InventoryItems
                        .Where(i => i.ProductId == prodA.Id)
                        .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, 19), ct);
                }
            });

            await using var scope = _fixture.CreateAsyncScope();
            var service = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook);

            var released = await service.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), CancellationToken.None);
            Assert.Equal(2, released);

            await using var verifyDb = _fixture.CreateDbContext();
            var reloadedA = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id);
            var reloadedB = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderB.Id);
            var invA = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id);
            var invB = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodB.Id);

            Assert.Equal(OrderStatus.Cancelled, reloadedA.OrderStatus);
            Assert.Equal(OrderStatus.Cancelled, reloadedB.OrderStatus);
            Assert.Equal(0, invA.Reserved);
            Assert.Equal(0, invB.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestB_ConcurrentWorkersProcessSameExpiredOrders_EachReleasedAtMostOnce()
    {
        var (store, prodA, prodB, orderA, orderB) = await SeedTwoExpiredOrdersAsync();
        try
        {
            var worker1Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var worker2Entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var hook1 = new DelegateOrderReleaseHook(async (orderId, attempt, ct) =>
            {
                if (orderId == orderA.Id && attempt == 1)
                {
                    worker1Entered.TrySetResult(true);
                    await releaseBarrier.Task.WaitAsync(ct);
                }
            });

            var hook2 = new DelegateOrderReleaseHook(async (orderId, attempt, ct) =>
            {
                if (orderId == orderA.Id && attempt == 1)
                {
                    worker2Entered.TrySetResult(true);
                    await releaseBarrier.Task.WaitAsync(ct);
                }
            });

            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var s1 = new OrderService(
                scope1.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope1.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope1.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook1);
            var s2 = new OrderService(
                scope2.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope2.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope2.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook2);

            var now = DateTimeOffset.UtcNow.AddMinutes(20);
            var t1 = s1.ReleaseExpiredReservationsAsync(now, CancellationToken.None);
            var t2 = s2.ReleaseExpiredReservationsAsync(now, CancellationToken.None);

            await Task.WhenAll(worker1Entered.Task, worker2Entered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            releaseBarrier.TrySetResult(true);

            var counts = await Task.WhenAll(t1, t2);
            Assert.Equal(2, counts.Sum());

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(OrderStatus.Cancelled, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id)).OrderStatus);
            Assert.Equal(OrderStatus.Cancelled, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderB.Id)).OrderStatus);
            Assert.Equal(0, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
            Assert.Equal(0, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodB.Id)).Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestC_FailureDuringAtomicOperation_RollsBackBothOrderAndInventory()
    {
        var (store, prodA, _, orderA, orderB) = await SeedTwoExpiredOrdersAsync();
        try
        {
            var hook = new DelegateOrderReleaseHook((orderId, _, _) =>
            {
                if (orderId == orderA.Id)
                {
                    throw new InvalidOperationException("Simulated persistence boundary failure.");
                }

                return Task.CompletedTask;
            });

            await using var scope = _fixture.CreateAsyncScope();
            var service = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook);

            var released = await service.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), CancellationToken.None);
            Assert.Equal(1, released);

            await using var verifyDb = _fixture.CreateDbContext();
            var reloadedA = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id);
            var reloadedB = await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderB.Id);
            var invA = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id);

            Assert.Equal(OrderStatus.Pending, reloadedA.OrderStatus);
            Assert.Equal(2, invA.Reserved);
            Assert.Equal(OrderStatus.Cancelled, reloadedB.OrderStatus);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestDAndE_RetryReloadsWinnerStateWithoutDoubleRelease_And_SubsequentSweepIsIdempotent()
    {
        var (store, prodA, prodB, orderA, orderB) = await SeedTwoExpiredOrdersAsync(qtyA: 2, qtyB: 3, extraReservedA: 2);
        try
        {
            var hook = new DelegateOrderReleaseHook(async (orderId, attempt, ct) =>
            {
                if (orderId == orderA.Id && attempt == 1)
                {
                    // Competing worker cancels Order A and releases its 2 units (leaving extraReservedA = 2 untouched)
                    await using var winnerScope = _fixture.CreateAsyncScope();
                    var winnerService = winnerScope.ServiceProvider.GetRequiredService<IOrderService>();
                    await winnerService.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), ct);
                }
            });

            await using var scope = _fixture.CreateAsyncScope();
            var losingService = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook);

            var releasedByLoser = await losingService.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), CancellationToken.None);
            Assert.Equal(0, releasedByLoser);

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(OrderStatus.Cancelled, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id)).OrderStatus);
            Assert.Equal(OrderStatus.Cancelled, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderB.Id)).OrderStatus);
            // Extra 2 reserved units on Prod A were NOT double-released!
            Assert.Equal(2, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
            Assert.Equal(0, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodB.Id)).Reserved);

            // Test E: Subsequent sweep does nothing
            var secondSweep = await losingService.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), CancellationToken.None);
            Assert.Equal(0, secondSweep);
            Assert.Equal(2, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestF_CancellationDuringProcessing_DoesNotLeavePartialState()
    {
        var (store, prodA, _, orderA, _) = await SeedTwoExpiredOrdersAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            var hook = new DelegateOrderReleaseHook((orderId, _, _) =>
            {
                if (orderId == orderA.Id)
                {
                    cts.Cancel();
                }

                return Task.CompletedTask;
            });

            await using var scope = _fixture.CreateAsyncScope();
            var service = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), cts.Token));

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(OrderStatus.Pending, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id)).OrderStatus);
            Assert.Equal(2, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
            Assert.Equal(1, await verifyDb.Set<OrderStatusHistory>().AsNoTracking().CountAsync(h => h.OrderId == orderA.Id));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestG_CancellationBeforeProcessing_ThrowsOperationCanceledWithoutTouchingOrders()
    {
        var (store, prodA, _, orderA, _) = await SeedTwoExpiredOrdersAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await using var scope = _fixture.CreateAsyncScope();
            var service = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), cts.Token));

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(OrderStatus.Pending, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id)).OrderStatus);
            Assert.Equal(2, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    private sealed class CancelAfterSaveChangesInterceptor(CancellationTokenSource cts)
        : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            cts.Cancel();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InterceptedScopeFactory(
        IServiceScopeFactory inner,
        string connectionString,
        Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor interceptor) : IServiceScopeFactory
    {
        public IServiceScope CreateScope()
        {
            var services = new ServiceCollection();
            services.AddDbContext<RetailFlowDbContext>(o => o.UseSqlServer(connectionString).AddInterceptors(interceptor));
            var sp = services.BuildServiceProvider();
            return new OwnedScope(sp, inner.CreateScope());
        }

        private sealed class OwnedScope(ServiceProvider sp, IServiceScope innerScope) : IServiceScope
        {
            public IServiceProvider ServiceProvider => sp;
            public void Dispose()
            {
                innerScope.Dispose();
                sp.Dispose();
            }
        }
    }

    [Fact]
    public async Task TestH_CancellationAfterSaveChangesBeforeCommit_RollsBackAtomically()
    {
        var (store, prodA, _, orderA, _) = await SeedTwoExpiredOrdersAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            var interceptor = new CancelAfterSaveChangesInterceptor(cts);

            await using var scope = _fixture.CreateAsyncScope();
            var interceptedFactory = new InterceptedScopeFactory(
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                _fixture.ConnectionString,
                interceptor);

            var service = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                interceptedFactory,
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), cts.Token));

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(OrderStatus.Pending, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id)).OrderStatus);
            Assert.Equal(2, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
            Assert.Equal(1, await verifyDb.Set<OrderStatusHistory>().AsNoTracking().CountAsync(h => h.OrderId == orderA.Id));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestI_CancellationBetweenOrdersInBatch_PreservesCommittedFirstAndLeavesSecondUntouched()
    {
        var (store, prodA, prodB, orderA, orderB) = await SeedTwoExpiredOrdersAsync();
        try
        {
            using var cts = new CancellationTokenSource();
            var hook = new DelegateOrderReleaseHook((orderId, _, _) =>
            {
                if (orderId == orderB.Id)
                {
                    cts.Cancel();
                }

                return Task.CompletedTask;
            });

            await using var scope = _fixture.CreateAsyncScope();
            var service = new OrderService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<OrderService>>(),
                TimeProvider.System,
                hook);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(20), cts.Token));

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(OrderStatus.Cancelled, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA.Id)).OrderStatus);
            Assert.Equal(0, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodA.Id)).Reserved);
            Assert.Equal(OrderStatus.Pending, (await verifyDb.Orders.AsNoTracking().SingleAsync(o => o.Id == orderB.Id)).OrderStatus);
            Assert.Equal(3, (await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodB.Id)).Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }
}
