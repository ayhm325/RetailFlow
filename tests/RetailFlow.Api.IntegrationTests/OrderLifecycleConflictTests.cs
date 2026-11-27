using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;
using RetailFlow.Infrastructure.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class OrderLifecycleConflictTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public OrderLifecycleConflictTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class BarrierSaveChangesInterceptor : SaveChangesInterceptor
    {
        private readonly Func<DbContext, CancellationToken, Task> _onSaving;

        public BarrierSaveChangesInterceptor(Func<DbContext, CancellationToken, Task> onSaving)
        {
            _onSaving = onSaving;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null)
            {
                await _onSaving(eventData.Context, cancellationToken);
            }

            return result;
        }
    }

    private RetailFlowDbContext CreateInterceptedDbContext(SaveChangesInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<RetailFlowDbContext>()
            .UseSqlServer(_fixture.ConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        return new RetailFlowDbContext(options);
    }

    private async Task<(Guid StoreId, string StoreSlug, Guid ProductId, Guid OrderId, Guid CustomerId)> SeedPendingOrderInDbAsync(
        int onHand = 10,
        int qty = 2)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        // Offset CreatedAtUtc by +1h so parallel reservation-expiry sweeps (which advance clock by +20m) never cancel this test order.
        var now = DateTimeOffset.UtcNow.AddHours(1);
        var store = Store.Create(Guid.NewGuid(), "Conflict Shop", $"conf-{suffix}", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var product = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Beans", 10.000m, settings, Jod, ProductStatus.Published, now);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, product.Id, onHand: onHand, reserved: qty);
        var customerId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var order = Order.Create(
            orderId,
            store,
            settings,
            Jod,
            customerId,
            $"ORD-{suffix}",
            FulfillmentMode.Pickup,
            [OrderItem.Create(Guid.NewGuid(), store.Id, orderId, product.Id, product.Name, product.Sku, product.Price, qty, Jod)],
            0m,
            null,
            null,
            null,
            now);

        await using var db = _fixture.CreateDbContext();
        db.Stores.Add(store);
        db.StoreSettings.Add(settings);
        db.Categories.Add(cat);
        db.Products.Add(product);
        db.InventoryItems.Add(inv);
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        return (store.Id, store.Slug, product.Id, orderId, customerId);
    }

    private async Task<(Guid AdminId, Guid StoreId, string StoreSlug, string MerchantEmail, Guid ProductId, Guid OrderId, HttpClient MerchantClient, HttpClient CustomerClient)> SetupAuthenticatedOrderFlowAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var adminEmail = $"admin-oc-{suffix}@retailflow.test";

        await using (var scope = _fixture.CreateAsyncScope())
        {
            var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
            scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());
            var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<RetailFlowIdentityRole>>();
            if (!await roleManager.RoleExistsAsync(IdentityRoleNames.PlatformAdmin))
            {
                await roleManager.CreateAsync(new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.PlatformAdminRoleId,
                    Name = IdentityRoleNames.PlatformAdmin,
                    NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                });
            }

            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<RetailFlowIdentityUser>>();
            var admin = new RetailFlowIdentityUser
            {
                Id = adminId,
                UserName = adminId.ToString("N"),
                Email = adminEmail,
                DisplayName = "Platform Admin",
                StoreId = null,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            Assert.True((await userManager.CreateAsync(admin, "Adm1nPassphrase!99")).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin)).Succeeded);
        }

        using var adminClient = _fixture.CreateHttpsClient();
        adminClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var adminLogin = await adminClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(adminEmail, "Adm1nPassphrase!99"));
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);

        using var createStoreResp = await adminClient.PostAsJsonAsync(
            "/api/platform/stores",
            new CreateStoreCommand("Conflict Roast", $"conf-roast-{suffix}", StoreType.Physical, "JOD", "Asia/Amman", true, true, "Amman", "Dock"));
        Assert.Equal(HttpStatusCode.Created, createStoreResp.StatusCode);
        var store = (await createStoreResp.Content.ReadFromJsonAsync<PlatformStoreDto>(JsonOptions))!;

        using var approveResp = await adminClient.PostAsync($"/api/platform/stores/{store.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approveResp.StatusCode);

        var merchantEmail = $"merch-oc-{suffix}@example.com";
        using var createMerchResp = await adminClient.PostAsJsonAsync(
            $"/api/platform/stores/{store.Id}/merchant-owner",
            new CreateMerchantUserCommand(merchantEmail, "Merchant", "MerchPassphrase!1"));
        Assert.Equal(HttpStatusCode.Created, createMerchResp.StatusCode);

        var merchantClient = _fixture.CreateHttpsClient();
        merchantClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var merchLogin = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{store.Slug}/auth/login",
            new LoginCommand(merchantEmail, "MerchPassphrase!1"));
        Assert.Equal(HttpStatusCode.OK, merchLogin.StatusCode);

        using var catResp = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{store.Slug}/merchant/categories",
            new CreateCategoryCommand("Coffee", "coffee", 1, true));
        Assert.Equal(HttpStatusCode.Created, catResp.StatusCode);
        var category = (await catResp.Content.ReadFromJsonAsync<MerchantCategoryDto>(JsonOptions))!;

        using var prodResp = await merchantClient.PostAsJsonAsync(
            $"/api/stores/{store.Slug}/merchant/products",
            new CreateProductCommand(category.Id, "Kenya AA", "Bright", $"KAA-{suffix}", 12.000m, ProductStatus.Published, 10));
        Assert.Equal(HttpStatusCode.Created, prodResp.StatusCode);
        var product = (await prodResp.Content.ReadFromJsonAsync<MerchantProductDto>(JsonOptions))!;

        var customerClient = _fixture.CreateHttpsClient();
        customerClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var custReg = await customerClient.PostAsJsonAsync(
            $"/api/stores/{store.Slug}/auth/register",
            new RegisterCustomerCommand($"cust-oc-{suffix}@example.com", "Customer", "CustPassphrase!1"));
        Assert.Equal(HttpStatusCode.Created, custReg.StatusCode);

        using var cartResp = await customerClient.PutAsJsonAsync(
            $"/api/stores/{store.Slug}/cart/items",
            new UpsertCartItemCommand(product.Id, 2));
        Assert.Equal(HttpStatusCode.OK, cartResp.StatusCode);

        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
        checkoutReq.Headers.Add("Idempotency-Key", $"idem-oc-{suffix}");
        checkoutReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Pickup, null, null));
        using var checkoutResp = await customerClient.SendAsync(checkoutReq);
        Assert.Equal(HttpStatusCode.Created, checkoutResp.StatusCode);
        var order = (await checkoutResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;

        return (adminId, store.Id, store.Slug, merchantEmail, product.Id, order.Id, merchantClient, customerClient);
    }

    private static async Task AssertConflictProblemDetailsAsync(
        HttpResponseMessage response,
        string expectedCode,
        string expectedInstance)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("about:blank", root.GetProperty("type").GetString());
        Assert.Equal("Conflict", root.GetProperty("title").GetString());
        Assert.Equal(409, root.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, root.GetProperty("code").GetString());
        Assert.Equal(expectedInstance, root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Api_OrderStateConflicts_ReturnUniform409ProblemDetails_AndNotFoundRemains404()
    {
        var (adminId, storeId, slug, _, _, orderId, merchantClient, customerClient) = await SetupAuthenticatedOrderFlowAsync();

        try
        {
            // Collect COD before Confirm -> 409 order_state_conflict
            using (var collectEarly = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{orderId}/collect-payment", null))
            {
                await AssertConflictProblemDetailsAsync(collectEarly, "order_state_conflict", $"/api/stores/{slug}/merchant/orders/{orderId}/collect-payment");
            }

            // Confirm succeeds -> 200
            using (var confirmOk = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{orderId}/confirm", null))
            {
                Assert.Equal(HttpStatusCode.OK, confirmOk.StatusCode);
            }

            // Duplicate Confirm -> 409 order_state_conflict
            using (var confirmDup = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{orderId}/confirm", null))
            {
                await AssertConflictProblemDetailsAsync(confirmDup, "order_state_conflict", $"/api/stores/{slug}/merchant/orders/{orderId}/confirm");
            }

            // Reject after Confirm -> 409 order_state_conflict
            using (var rejectAfterConfirm = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{orderId}/reject",
                new { Reason = "Too late" }))
            {
                await AssertConflictProblemDetailsAsync(rejectAfterConfirm, "order_state_conflict", $"/api/stores/{slug}/merchant/orders/{orderId}/reject");
            }

            // Customer Cancel after Confirm -> 409 order_state_conflict
            using (var cancelAfterConfirm = await customerClient.PostAsync($"/api/stores/{slug}/orders/{orderId}/cancel", null))
            {
                await AssertConflictProblemDetailsAsync(cancelAfterConfirm, "order_state_conflict", $"/api/stores/{slug}/orders/{orderId}/cancel");
            }

            // Invalid Fulfillment transition (Unfulfilled -> PickedUp) -> 409 order_state_conflict
            using (var invalidFulfillment = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{orderId}/fulfillment",
                new { NextStatus = FulfillmentStatus.PickedUp }))
            {
                await AssertConflictProblemDetailsAsync(invalidFulfillment, "order_state_conflict", $"/api/stores/{slug}/merchant/orders/{orderId}/fulfillment");
            }

            // Advance to PickedUp and Collect COD
            using (var prep = await merchantClient.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{orderId}/fulfillment", new { NextStatus = FulfillmentStatus.Preparing }))
                Assert.Equal(HttpStatusCode.OK, prep.StatusCode);
            using (var ready = await merchantClient.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{orderId}/fulfillment", new { NextStatus = FulfillmentStatus.ReadyForPickup }))
                Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
            using (var pickedUp = await merchantClient.PostAsJsonAsync($"/api/stores/{slug}/merchant/orders/{orderId}/fulfillment", new { NextStatus = FulfillmentStatus.PickedUp }))
                Assert.Equal(HttpStatusCode.OK, pickedUp.StatusCode);

            using (var collectOk = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{orderId}/collect-payment", null))
                Assert.Equal(HttpStatusCode.OK, collectOk.StatusCode);

            // Duplicate COD Collection -> 409 order_state_conflict
            using (var collectDup = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{orderId}/collect-payment", null))
            {
                await AssertConflictProblemDetailsAsync(collectDup, "order_state_conflict", $"/api/stores/{slug}/merchant/orders/{orderId}/collect-payment");
            }

            // Non-existent order -> 404 NotFound
            var missingOrderId = Guid.NewGuid();
            using var notFoundResp = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{missingOrderId}/confirm", null);
            Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        }
        finally
        {
            merchantClient.Dispose();
            customerClient.Dispose();
            await _fixture.DeleteStoresAsync(storeId);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Api_GeneralInvalidOperationException_Remains500_NotMappedTo409()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "localhost",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["Email:RequireConfiguredInProduction"] = "false"
                    };
                    TestDataProtectionCertHelper.ApplyTestCertificate(values);
                    config.AddInMemoryCollection(values);
                });
                builder.Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.Run(_ => throw new InvalidOperationException("Simulated internal invariant bug"));
                });
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var resp = await client.GetAsync("/test-unexpected-invalid-op");

        Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
        Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Simulated internal invariant bug", body, StringComparison.Ordinal);
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("internal_server_error", doc.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SqlConcurrency_ConfirmVsReject_OnlyOneWins_AndLoserThrowsOrderConcurrencyConflictWithoutPartialState(bool confirmWins)
    {
        var (storeId, _, productId, orderId, _) = await SeedPendingOrderInDbAsync(onHand: 10, qty: 2);

        try
        {
            // Both contexts must load the Pending order with the same RowVersion before either saves.
            var winnerLoaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loserReadyToSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var winnerSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var winnerInterceptor = new BarrierSaveChangesInterceptor(async (_, ct) =>
            {
                winnerLoaded.TrySetResult();
                await loserReadyToSave.Task.WaitAsync(ct);
            });

            var loserInterceptor = new BarrierSaveChangesInterceptor(async (_, ct) =>
            {
                loserReadyToSave.TrySetResult();
                await winnerSaved.Task.WaitAsync(ct);
            });

            await using var winnerDb = CreateInterceptedDbContext(winnerInterceptor);
            await using var loserDb = CreateInterceptedDbContext(loserInterceptor);
            await using var scope = _fixture.CreateAsyncScope();
            var scopeFactory = scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>();

            var winnerService = new OrderService(winnerDb, scopeFactory);
            var loserService = new OrderService(loserDb, scopeFactory);
            var actorId = Guid.NewGuid();

            Task<OrderDto?> winnerTask = confirmWins
                ? RunWinnerAsync(() => winnerService.ConfirmOrderAsync(storeId, actorId, orderId, CancellationToken.None), winnerSaved)
                : RunWinnerAsync(() => winnerService.RejectOrderAsync(storeId, actorId, orderId, "Out of stock", CancellationToken.None), winnerSaved);

            Task<OrderDto?> loserTask = confirmWins
                ? loserService.RejectOrderAsync(storeId, actorId, orderId, "Out of stock", CancellationToken.None)
                : loserService.ConfirmOrderAsync(storeId, actorId, orderId, CancellationToken.None);

            var winnerResult = await winnerTask;
            Assert.NotNull(winnerResult);

            var conflictEx = await Assert.ThrowsAsync<OrderConcurrencyConflictException>(() => loserTask);
            Assert.Equal(orderId, conflictEx.OrderId);
            Assert.Equal(confirmWins ? nameof(OrderService.RejectOrderAsync) : nameof(OrderService.ConfirmOrderAsync), conflictEx.Operation);

            // Verify DB state: only winner persisted, history has exactly 2 entries, inventory matches winner.
            await using var verifyDb = _fixture.CreateDbContext();
            var finalOrder = await verifyDb.Orders
                .AsNoTracking()
                .Include(o => o.History)
                .SingleAsync(o => o.Id == orderId);
            var finalInv = await verifyDb.InventoryItems
                .AsNoTracking()
                .SingleAsync(i => i.ProductId == productId);

            Assert.Equal(confirmWins ? OrderStatus.Confirmed : OrderStatus.Rejected, finalOrder.OrderStatus);
            Assert.Equal(2, finalOrder.History.Count);
            Assert.Equal(confirmWins ? 2 : 0, finalInv.Reserved);
            Assert.Equal(10, finalInv.OnHand);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }

        static async Task<OrderDto?> RunWinnerAsync(Func<Task<OrderDto?>> action, TaskCompletionSource winnerSaved)
        {
            try
            {
                return await action();
            }
            finally
            {
                winnerSaved.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task SqlConcurrency_InventoryRowVersionRaceOnCustomerCancel_RollsBackOrderHistoryAndInventoryAtomically()
    {
        var (storeId, _, productId, orderId, customerId) = await SeedPendingOrderInDbAsync(onHand: 10, qty: 3);

        try
        {
            var interceptor = new BarrierSaveChangesInterceptor(async (_, ct) =>
            {
                // Simulate concurrent inventory modification after OrderService loaded Order & InventoryItem
                await using var competingDb = _fixture.CreateDbContext();
                await competingDb.InventoryItems
                    .Where(i => i.ProductId == productId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, 11), ct);
            });

            await using var db = CreateInterceptedDbContext(interceptor);
            await using var scope = _fixture.CreateAsyncScope();
            var service = new OrderService(db, scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>());

            var ex = await Assert.ThrowsAsync<OrderConcurrencyConflictException>(() =>
                service.CancelByCustomerAsync(storeId, customerId, orderId, "Cancel race", CancellationToken.None));
            Assert.Equal(orderId, ex.OrderId);
            Assert.Equal(nameof(OrderService.CancelByCustomerAsync), ex.Operation);

            // Verify full atomic rollback: Order is still Pending, History has only 1 entry, Inventory.Reserved is still 3
            await using var verifyDb = _fixture.CreateDbContext();
            var finalOrder = await verifyDb.Orders
                .AsNoTracking()
                .Include(o => o.History)
                .SingleAsync(o => o.Id == orderId);
            var finalInv = await verifyDb.InventoryItems
                .AsNoTracking()
                .SingleAsync(i => i.ProductId == productId);

            Assert.Equal(OrderStatus.Pending, finalOrder.OrderStatus);
            Assert.Single(finalOrder.History);
            Assert.Equal(3, finalInv.Reserved);
            Assert.Equal(11, finalInv.OnHand);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task Api_OrderConcurrencyConflict_Returns409WithOrderConcurrencyConflictCode()
    {
        var (adminId, storeId, slug, merchantEmail, _, orderId, merchantClient, customerClient) = await SetupAuthenticatedOrderFlowAsync();

        try
        {
            await using var raceFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment(Environments.Production);
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        var values = new Dictionary<string, string?>
                        {
                            ["AllowedHosts"] = "localhost",
                            ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                            ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                            ["Email:RequireConfiguredInProduction"] = "false"
                        };
                        TestDataProtectionCertHelper.ApplyTestCertificate(values);
                        config.AddInMemoryCollection(values);
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.AddScoped<IOrderService>(sp =>
                        {
                            var interceptor = new BarrierSaveChangesInterceptor(async (_, ct) =>
                            {
                                await using var competingDb = _fixture.CreateDbContext();
                                await competingDb.Orders
                                    .Where(o => o.Id == orderId)
                                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.CancellationReason, "Concurrent bump"), ct);
                            });
                            var interceptedDb = CreateInterceptedDbContext(interceptor);
                            return new OrderService(interceptedDb, sp.GetRequiredService<IServiceScopeFactory>());
                        });
                    });
                });

            using var raceMerchantClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
            raceMerchantClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var loginResp = await raceMerchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(merchantEmail, "MerchPassphrase!1"));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

            using var confirmConflictResp = await raceMerchantClient.PostAsync(
                $"/api/stores/{slug}/merchant/orders/{orderId}/confirm",
                null);

            await AssertConflictProblemDetailsAsync(
                confirmConflictResp,
                "order_concurrency_conflict",
                $"/api/stores/{slug}/merchant/orders/{orderId}/confirm");
        }
        finally
        {
            merchantClient.Dispose();
            customerClient.Dispose();
            await _fixture.DeleteStoresAsync(storeId);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }
}
