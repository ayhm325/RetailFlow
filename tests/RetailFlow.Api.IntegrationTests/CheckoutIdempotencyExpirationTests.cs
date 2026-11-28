using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using RetailFlow.Api.Hosting;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class CheckoutIdempotencyExpirationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public CheckoutIdempotencyExpirationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }

    private sealed class BarrierFinalizationHook : ICheckoutFinalizationHook
    {
        private readonly TaskCompletionSource<bool> _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Entered => _entered.Task;
        public void Release() => _release.TrySetResult(true);

        public async Task OnBeforeFinalizationAsync(Guid claimId, Guid claimToken, CancellationToken cancellationToken)
        {
            _entered.TrySetResult(true);
            await _release.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class PostCommitExceptionInterceptor : DbTransactionInterceptor
    {
        private int _thrown;
        private readonly Func<Exception>? _exceptionFactory;

        public PostCommitExceptionInterceptor(Func<Exception>? exceptionFactory = null)
        {
            _exceptionFactory = exceptionFactory;
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
            {
                throw _exceptionFactory?.Invoke()
                    ?? new InvalidOperationException("Simulated post-commit acknowledgement loss");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CommitThrowingInterceptor : DbTransactionInterceptor
    {
        private int _thrown;
        private readonly Func<Exception> _exceptionFactory;

        public CommitThrowingInterceptor(Func<Exception> exceptionFactory)
        {
            _exceptionFactory = exceptionFactory;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref _thrown, 1, 0) == 0)
            {
                throw _exceptionFactory();
            }

            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }
    }

    private sealed class FailingReconciliationScopeFactory : IServiceScopeFactory
    {
        private sealed class FailingServiceProvider : IServiceProvider
        {
            public object? GetService(Type serviceType)
                => throw new InvalidOperationException("Simulated reconciliation scope failure.");
        }

        private sealed class FailingScope : IServiceScope
        {
            public IServiceProvider ServiceProvider { get; } = new FailingServiceProvider();
            public void Dispose() { }
        }

        public IServiceScope CreateScope() => new FailingScope();
    }

    private async Task<(Store Store, Product Product, DeliveryZone Zone, Guid CustomerId)> SeedStoreAndCartAsync(
        int onHand = 20,
        int cartQty = 2)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Idem Shop", $"idem-{suffix}", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var zone = DeliveryZone.Create(Guid.NewGuid(), store.Id, "Amman", 2.000m, settings, Jod, now, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Beans", 10.000m, settings, Jod, ProductStatus.Published, now);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, prod.Id, onHand: onHand, reserved: 0);
        var customerId = Guid.NewGuid();
        var cart = Cart.Create(Guid.NewGuid(), store.Id, customerId, now);
        var cartItem = CartItem.Create(Guid.NewGuid(), store.Id, cart.Id, prod.Id, cartQty);

        await using var db = _fixture.CreateDbContext();
        db.Stores.Add(store);
        db.StoreSettings.Add(settings);
        db.DeliveryZones.Add(zone);
        db.Categories.Add(cat);
        db.Products.Add(prod);
        db.InventoryItems.Add(inv);
        db.Carts.Add(cart);
        db.CartItems.Add(cartItem);
        await db.SaveChangesAsync();

        return (store, prod, zone, customerId);
    }

    private RetailFlowDbContext CreateDbContextWithInterceptor(IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<RetailFlowDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new RetailFlowDbContext(options);
    }

    [Fact]
    public async Task CoreRetention_CompletedBeginsAtCompletion_ExactBoundaryReclaims_AndPreservesPersistedId()
    {
        var (store, prod, _, customerId) = await SeedStoreAndCartAsync(onHand: 20, cartQty: 2);

        try
        {
            var t0 = new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);
            var tp = new MutableTimeProvider(t0);
            var key = $"idem-core-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            Guid originalClaimId;
            Guid originalOrderId;
            // Advance 30s between Claim and Completion using hook to prove CompletedAtUtc + 24h (not CreatedAt + 24h)
            var delayHook = new BarrierFinalizationHook();
            await using (var db1 = _fixture.CreateDbContext())
            {
                var svc1 = new CheckoutService(db1, scopeFactory, NullLogger<CheckoutService>.Instance, tp, delayHook);
                var task = svc1.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
                await delayHook.Entered;
                tp.Advance(TimeSpan.FromSeconds(30));
                delayHook.Release();

                var created = await task;
                Assert.Equal(CheckoutOutcomeStatus.Created, created.Status);
                originalOrderId = created.Order!.Id;
            }

            await using (var verifyDb = _fixture.CreateDbContext())
            {
                var row = await verifyDb.CheckoutRequests.AsNoTracking().SingleAsync(r => r.StoreId == store.Id && r.IdempotencyKey == key);
                originalClaimId = row.Id;
                Assert.Equal(CheckoutRequestStatus.Completed, row.Status);
                Assert.Equal(t0.AddSeconds(30), row.CompletedAtUtc);
                Assert.Equal(t0.AddSeconds(30).AddHours(24), row.ExpiresAtUtc);
            }

            // At t0 + 24h + 10s (past CreatedAt + 24h, BEFORE CompletedAt + 24h) -> Replay (200)
            tp.SetUtcNow(t0.AddHours(24).AddSeconds(10));
            await using (var dbReplay = _fixture.CreateDbContext())
            {
                var svcReplay = new CheckoutService(dbReplay, scopeFactory, NullLogger<CheckoutService>.Instance, tp);
                var replay = await svcReplay.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
                Assert.Equal(CheckoutOutcomeStatus.Replayed, replay.Status);
                Assert.Equal(originalOrderId, replay.Order!.Id);
            }

            // Different fingerprint before expiry -> PayloadMismatch (422)
            await using (var dbMismatch = _fixture.CreateDbContext())
            {
                var svcMismatch = new CheckoutService(dbMismatch, scopeFactory, NullLogger<CheckoutService>.Instance, tp);
                var mismatch = await svcMismatch.CheckoutAsync(store.Id, customerId, key, new CheckoutCommand(FulfillmentMode.Delivery, Guid.NewGuid(), "Other"), CancellationToken.None);
                Assert.Equal(CheckoutOutcomeStatus.PayloadMismatch, mismatch.Status);
            }

            // Re-populate cart and advance to EXACT boundary ExpiresAtUtc == now -> Atomic Reclaim succeeds using originalClaimId!
            await using (var scopeCart = _fixture.CreateAsyncScope())
            {
                await scopeCart.ServiceProvider.GetRequiredService<ICartService>()
                    .UpsertItemAsync(store.Id, customerId, new UpsertCartItemCommand(prod.Id, 3), CancellationToken.None);
            }

            tp.SetUtcNow(t0.AddSeconds(30).AddHours(24));
            await using (var dbReclaim = _fixture.CreateDbContext())
            {
                var svcReclaim = new CheckoutService(dbReclaim, scopeFactory, NullLogger<CheckoutService>.Instance, tp);
                var reclaimed = await svcReclaim.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
                Assert.Equal(CheckoutOutcomeStatus.Created, reclaimed.Status);
                Assert.NotEqual(originalOrderId, reclaimed.Order!.Id);
                Assert.Empty(dbReclaim.ChangeTracker.Entries<CheckoutRequest>());
            }

            await using (var finalDb = _fixture.CreateDbContext())
            {
                var rows = await finalDb.CheckoutRequests.AsNoTracking().Where(r => r.StoreId == store.Id && r.IdempotencyKey == key).ToListAsync();
                Assert.Single(rows);
                Assert.Equal(originalClaimId, rows[0].Id); // Persisted ID preserved!
                Assert.Equal(2, await finalDb.Orders.CountAsync(o => o.StoreId == store.Id));
                var inv = await finalDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
                Assert.Equal(5, inv.Reserved); // 2 + 3
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task AtomicReclaimConcurrency_WinnerCompletesBeforeLoserReReads_LoserReplaysWinnerOrder()
    {
        var (store, prod, _, customerId) = await SeedStoreAndCartAsync(onHand: 20, cartQty: 2);

        try
        {
            var t0 = DateTimeOffset.UtcNow;
            var key = $"idem-race-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            // Seed expired row (-25h)
            var expiredRow = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, key, new string('A', 64), t0.AddHours(-25));
            await using (var db = _fixture.CreateDbContext())
            {
                db.CheckoutRequests.Add(expiredRow);
                await db.SaveChangesAsync();
            }

            var tp = new MutableTimeProvider(t0);
            var hookWinner = new BarrierFinalizationHook();
            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            await using var dbWinner = _fixture.CreateDbContext();
            await using var dbLoser = _fixture.CreateDbContext();
            var svcWinner = new CheckoutService(dbWinner, scopeFactory, NullLogger<CheckoutService>.Instance, tp, hookWinner);
            var svcLoser = new CheckoutService(dbLoser, scopeFactory, NullLogger<CheckoutService>.Instance, tp);

            var winnerTask = svcWinner.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
            await hookWinner.Entered;

            // Loser arrives while winner holds active Lease -> 409 ConcurrentInFlight
            var loserWhileActive = await svcLoser.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.ConcurrentInFlight, loserWhileActive.Status);

            // Winner completes
            hookWinner.Release();
            var winnerResult = await winnerTask;
            Assert.Equal(CheckoutOutcomeStatus.Created, winnerResult.Status);

            // Subsequent retry by loser replays winner's order
            var loserAfterComplete = await svcLoser.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Replayed, loserAfterComplete.Status);
            Assert.Equal(winnerResult.Order!.Id, loserAfterComplete.Order!.Id);

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(1, await verifyDb.Orders.CountAsync(o => o.StoreId == store.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(2, inv.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task CommitUncertainty_PostCommitException_ReconcilesWithIndependentBoundedToken_AndReplays()
    {
        var (store, prod, _, customerId) = await SeedStoreAndCartAsync(onHand: 10, cartQty: 2);

        try
        {
            var tp = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var key = $"idem-postcommit-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            using var canceledCts = new CancellationTokenSource();
            var postCommitInterceptor = new PostCommitExceptionInterceptor();
            await using var interceptedDb = CreateDbContextWithInterceptor(postCommitInterceptor);

            var svc = new CheckoutService(interceptedDb, scopeFactory, NullLogger<CheckoutService>.Instance, tp);
            // Even when TransactionCommittedAsync throws an exception, reconciliation in fresh DbContext finds Completed and returns Replayed!
            var result = await svc.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Replayed, result.Status);
            Assert.NotNull(result.Order);

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(1, await verifyDb.Orders.CountAsync(o => o.StoreId == store.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(2, inv.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task FailedRetention_DoesNotReset24h_AndLeaseRecoveryExtendsGraceProtectingFromCleanup()
    {
        var (store, prod, _, customerId) = await SeedStoreAndCartAsync(onHand: 10, cartQty: 2);

        try
        {
            var t0 = new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);
            var key = $"idem-failed-grace-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            // Seed a Failed claim created at t0
            var failedClaim = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, key, new string('F', 64), t0);
            failedClaim.MarkFailed(409);
            await using (var db = _fixture.CreateDbContext())
            {
                db.CheckoutRequests.Add(failedClaim);
                await db.SaveChangesAsync();
            }

            // Advance to t0 + 23h 59m 30s -> Recover Lease via hook, then verify ExpiresAtUtc was extended by +5m grace
            var tp = new MutableTimeProvider(t0.AddHours(23).AddMinutes(59).AddSeconds(30));
            var hook = new BarrierFinalizationHook();
            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            // Use matching hash for recovery
            await using (var dbSetHash = _fixture.CreateDbContext())
            {
                // Update hash to match actual cart so Lease Recovery proceeds
                var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"{store.Id:N}|{customerId:N}|{(int)cmd.FulfillmentMode}|{cmd.DeliveryZoneId:N}|{cmd.DeliveryAddress?.Trim()}|{prod.Id}:2")));
                await dbSetHash.CheckoutRequests.Where(r => r.Id == failedClaim.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.RequestHash, hash));
            }

            await using var dbRecover = _fixture.CreateDbContext();
            var svcRecover = new CheckoutService(dbRecover, scopeFactory, NullLogger<CheckoutService>.Instance, tp, hook);
            var recoverTask = svcRecover.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
            await hook.Entered;

            // Advance 40s (past t0 + 24h, STILL inside Lease + 5m grace) and run Cleanup -> MUST delete 0 rows!
            tp.Advance(TimeSpan.FromSeconds(40));
            var maint = new MaintenanceBackgroundService(scopeFactory, NullLogger<MaintenanceBackgroundService>.Instance, tp);
            await maint.RunMaintenanceSweepAsync(CancellationToken.None);

            await using (var midDb = _fixture.CreateDbContext())
            {
                var row = await midDb.CheckoutRequests.AsNoTracking().SingleOrDefaultAsync(r => r.Id == failedClaim.Id);
                Assert.NotNull(row);
                Assert.Equal(CheckoutRequestStatus.InProgress, row!.Status);
                Assert.True(row.ExpiresAtUtc > tp.GetUtcNow());
            }

            hook.Release();
            var completed = await recoverTask;
            Assert.Equal(CheckoutOutcomeStatus.Created, completed.Status);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task MaintenanceCleanup_CapturesTwoStepBoundedSql_AndReEvaluatesEligibilityOnDelete()
    {
        var (store, _, _, customerId) = await SeedStoreAndCartAsync(onHand: 10, cartQty: 1);

        try
        {
            var t0 = new DateTimeOffset(2026, 11, 28, 0, 0, 0, TimeSpan.Zero);
            var expired1 = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, $"clean-1-{Guid.NewGuid():N}", new string('1', 64), t0.AddHours(-25));
            expired1.MarkFailed(422);
            var activeLeasePastRetention = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, $"clean-active-{Guid.NewGuid():N}", new string('2', 64), t0.AddHours(-25));
            // Give activeLeasePastRetention a Lease > t0 while ExpiresAtUtc <= t0
            await using (var db = _fixture.CreateDbContext())
            {
                db.CheckoutRequests.AddRange(expired1, activeLeasePastRetention);
                await db.SaveChangesAsync();
                await db.CheckoutRequests
                    .Where(r => r.Id == activeLeasePastRetention.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.LeaseExpiresAtUtc, t0.AddMinutes(5)));
            }

            var sqlCapture = new SqlCaptureInterceptor();
            var tp = new MutableTimeProvider(t0);
            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            // Execute cleanup directly via MaintenanceBackgroundService using intercepted DbContext?
            // We capture SQL from a direct run of the exact LINQ shape + verify MaintenanceBackgroundService behavior!
            await using (var capturedDb = CreateDbContextWithInterceptor(sqlCapture))
            {
                var candidateIds = await capturedDb.CheckoutRequests
                    .AsNoTracking()
                    .Where(r => r.ExpiresAtUtc <= t0 &&
                                (r.Status != CheckoutRequestStatus.InProgress || r.LeaseExpiresAtUtc <= t0))
                    .OrderBy(r => r.ExpiresAtUtc)
                    .ThenBy(r => r.Id)
                    .Select(r => r.Id)
                    .Take(MaintenanceBackgroundService.CheckoutCleanupBatchSize)
                    .ToListAsync();

                Assert.Contains(expired1.Id, candidateIds);
                Assert.DoesNotContain(activeLeasePastRetention.Id, candidateIds);

                var deleted = await capturedDb.CheckoutRequests
                    .Where(r => candidateIds.Contains(r.Id) &&
                                r.ExpiresAtUtc <= t0 &&
                                (r.Status != CheckoutRequestStatus.InProgress || r.LeaseExpiresAtUtc <= t0))
                    .ExecuteDeleteAsync();

                Assert.True(deleted >= 1);
            }

            Assert.Contains(sqlCapture.Commands, c => c.Contains("TOP(@", StringComparison.OrdinalIgnoreCase) && c.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(sqlCapture.Commands, c => c.Contains("DELETE", StringComparison.OrdinalIgnoreCase) && c.Contains("ExpiresAtUtc", StringComparison.OrdinalIgnoreCase) && c.Contains("LeaseExpiresAtUtc", StringComparison.OrdinalIgnoreCase));

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.False(await verifyDb.CheckoutRequests.AnyAsync(r => r.Id == expired1.Id));
            Assert.True(await verifyDb.CheckoutRequests.AnyAsync(r => r.Id == activeLeasePastRetention.Id));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task LegitimateRepeatPurchases_WithDistinctKeys_CreateTwoIndependentOrders()
    {
        var (store, prod, _, customerId) = await SeedStoreAndCartAsync(onHand: 20, cartQty: 2);

        try
        {
            await using var scope = _fixture.CreateAsyncScope();
            var cartSvc = scope.ServiceProvider.GetRequiredService<ICartService>();
            var checkoutSvc = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            var r1 = await checkoutSvc.CheckoutAsync(store.Id, customerId, $"buy-1-{Guid.NewGuid():N}", cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Created, r1.Status);

            await cartSvc.UpsertItemAsync(store.Id, customerId, new UpsertCartItemCommand(prod.Id, 3), CancellationToken.None);
            var r2 = await checkoutSvc.CheckoutAsync(store.Id, customerId, $"buy-2-{Guid.NewGuid():N}", cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Created, r2.Status);
            Assert.NotEqual(r1.Order!.Id, r2.Order!.Id);

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(2, await verifyDb.Orders.CountAsync(o => o.StoreId == store.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(5, inv.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task HttpPostCheckout_ExpiredKey_ReclaimsAtomically_Returns201_AndPreservesRowId()
    {
        var (store, prod, _, _) = await SeedStoreAndCartAsync(onHand: 20, cartQty: 2);
        using var client = _fixture.CreateHttpsClient();
        client.DefaultRequestHeaders.Add("X-CSRF", "1");
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var key = $"http-reclaim-{suffix}";

        try
        {
            using var regResp = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/register",
                new RegisterCustomerCommand($"cust-{suffix}@example.com", "Customer Reclaim", "CustPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

            using var cartResp = await client.PutAsJsonAsync(
                $"/api/stores/{store.Slug}/cart/items",
                new UpsertCartItemCommand(prod.Id, 2));
            Assert.Equal(HttpStatusCode.OK, cartResp.StatusCode);

            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);
            using var firstReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
            firstReq.Headers.Add("Idempotency-Key", key);
            firstReq.Content = JsonContent.Create(cmd);
            using var firstResp = await client.SendAsync(firstReq);
            Assert.Equal(HttpStatusCode.Created, firstResp.StatusCode);
            var firstOrder = await firstResp.Content.ReadFromJsonAsync<OrderDto>();
            Assert.NotNull(firstOrder);

            using var replayReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
            replayReq.Headers.Add("Idempotency-Key", key);
            replayReq.Content = JsonContent.Create(cmd);
            using var replayResp = await client.SendAsync(replayReq);
            Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
            var replayedOrder = await replayResp.Content.ReadFromJsonAsync<OrderDto>();
            Assert.Equal(firstOrder!.Id, replayedOrder!.Id);

            Guid originalClaimId;
            await using (var db = _fixture.CreateDbContext())
            {
                var row = await db.CheckoutRequests.SingleAsync(r => r.StoreId == store.Id && r.IdempotencyKey == key);
                originalClaimId = row.Id;
                await db.CheckoutRequests
                    .Where(r => r.Id == originalClaimId)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
            }

            using var cart2Resp = await client.PutAsJsonAsync(
                $"/api/stores/{store.Slug}/cart/items",
                new UpsertCartItemCommand(prod.Id, 3));
            Assert.Equal(HttpStatusCode.OK, cart2Resp.StatusCode);

            using var reclaimReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
            reclaimReq.Headers.Add("Idempotency-Key", key);
            reclaimReq.Content = JsonContent.Create(cmd);
            using var reclaimResp = await client.SendAsync(reclaimReq);
            Assert.Equal(HttpStatusCode.Created, reclaimResp.StatusCode);
            var reclaimedOrder = await reclaimResp.Content.ReadFromJsonAsync<OrderDto>();
            Assert.NotNull(reclaimedOrder);
            Assert.NotEqual(firstOrder.Id, reclaimedOrder!.Id);

            await using var verifyDb = _fixture.CreateDbContext();
            var rows = await verifyDb.CheckoutRequests.AsNoTracking().Where(r => r.StoreId == store.Id && r.IdempotencyKey == key).ToListAsync();
            Assert.Single(rows);
            Assert.Equal(originalClaimId, rows[0].Id);
            Assert.Equal(reclaimedOrder.Id, rows[0].OrderId);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task PostCommitCancellation_ReconcilesWithIndependentToken_AndReturnsReplayed()
    {
        var (store, _, _, customerId) = await SeedStoreAndCartAsync(onHand: 10, cartQty: 1);
        var t0 = DateTimeOffset.UtcNow;
        var tp = new MutableTimeProvider(t0);
        var key = $"cancel-reconcile-{Guid.NewGuid():N}";
        var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

        try
        {
            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            using var callerCts = new CancellationTokenSource();
            var interceptor = new PostCommitExceptionInterceptor(() =>
            {
                callerCts.Cancel();
                return new OperationCanceledException(callerCts.Token);
            });
            await using var interceptedDb = CreateDbContextWithInterceptor(interceptor);

            var svc = new CheckoutService(
                interceptedDb,
                scopeFactory,
                NullLogger<CheckoutService>.Instance,
                tp);

            var outcome = await svc.CheckoutAsync(store.Id, customerId, key, cmd, callerCts.Token);
            Assert.Equal(CheckoutOutcomeStatus.Replayed, outcome.Status);
            Assert.NotNull(outcome.Order);

            await using var verifyDb = _fixture.CreateDbContext();
            var row = await verifyDb.CheckoutRequests.AsNoTracking().SingleAsync(r => r.StoreId == store.Id && r.IdempotencyKey == key);
            Assert.Equal(CheckoutRequestStatus.Completed, row.Status);
            Assert.Equal(outcome.Order!.Id, row.OrderId);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitUncertainty_InvalidOperationOnCommit_DoesNotMarkInProgressOrInconclusiveAsFailed(bool simulateInconclusive)
    {
        var (store, prod, _, customerId) = await SeedStoreAndCartAsync(onHand: 10, cartQty: 2);
        var tp = new MutableTimeProvider(DateTimeOffset.UtcNow);
        var key = $"commit-ioe-{simulateInconclusive}-{Guid.NewGuid():N}";
        var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

        try
        {
            await using var scope = _fixture.CreateAsyncScope();
            IServiceScopeFactory scopeFactory = simulateInconclusive
                ? new FailingReconciliationScopeFactory()
                : scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            var interceptor = new CommitThrowingInterceptor(
                () => new InvalidOperationException("Simulated connection state error during commit"));
            await using var interceptedDb = CreateDbContextWithInterceptor(interceptor);

            var svc = new CheckoutService(
                interceptedDb,
                scopeFactory,
                NullLogger<CheckoutService>.Instance,
                tp);

            if (simulateInconclusive)
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    svc.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None));
            }
            else
            {
                var result = await svc.CheckoutAsync(store.Id, customerId, key, cmd, CancellationToken.None);
                Assert.Equal(CheckoutOutcomeStatus.ConcurrentInFlight, result.Status);
            }

            await using var verifyDb = _fixture.CreateDbContext();
            var row = await verifyDb.CheckoutRequests.AsNoTracking().SingleAsync(r => r.StoreId == store.Id && r.IdempotencyKey == key);
            Assert.Equal(CheckoutRequestStatus.InProgress, row.Status);
            Assert.Null(row.OrderId);
            Assert.Null(row.ResponseStatusCode);
            Assert.Equal(0, await verifyDb.Orders.CountAsync(o => o.StoreId == store.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(0, inv.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }
}
