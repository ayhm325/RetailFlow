using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

public sealed class CheckoutLeaseAndCookieScopeTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public CheckoutLeaseAndCookieScopeTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class UnscopedProtectedEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapGet("/test-unscoped-protected", () => Results.Ok("leaked"))
                    .RequireAuthorization();
            });
        };
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider(DateTimeOffset initialUtcNow) => _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
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

    private async Task<(Store Store, Product Product, Guid CustomerId)> SeedStoreWithCartAsync(int onHand, int cartQty)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Fence Shop", $"fence-{suffix}", StoreType.Physical, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Espresso", 12.000m, settings, Jod, ProductStatus.Published, now);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, prod.Id, onHand: onHand, reserved: 0);
        var customerId = Guid.NewGuid();
        var cart = Cart.Create(Guid.NewGuid(), store.Id, customerId, now);
        var cartItem = CartItem.Create(Guid.NewGuid(), store.Id, cart.Id, prod.Id, cartQty);

        await using var db = _fixture.CreateDbContext();
        db.Stores.Add(store);
        db.StoreSettings.Add(settings);
        db.Categories.Add(cat);
        db.Products.Add(prod);
        db.InventoryItems.Add(inv);
        db.Carts.Add(cart);
        db.CartItems.Add(cartItem);
        await db.SaveChangesAsync();

        return (store, prod, customerId);
    }

    [Fact]
    public async Task ScenarioA_StaleWorkerWakesAfterWorkerBReclaimsAndCompletes_RejectedWithoutDuplicateOrder()
    {
        var (store, prod, customerId) = await SeedStoreWithCartAsync(onHand: 10, cartQty: 2);
        try
        {
            var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var hookA = new BarrierFinalizationHook();
            var idempotencyKey = $"fence-a-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scopeA = _fixture.CreateAsyncScope();
            var serviceA = new CheckoutService(
                scopeA.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scopeA.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scopeA.ServiceProvider.GetRequiredService<ILogger<CheckoutService>>(),
                timeProvider,
                hookA);

            // 1. Worker A acquires ClaimToken A and pauses right before finalization
            var taskA = serviceA.CheckoutAsync(store.Id, customerId, idempotencyKey, cmd, CancellationToken.None);
            await hookA.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            // 2. Lease expires (61s) and Worker B reclaims and completes Order #1
            timeProvider.Advance(TimeSpan.FromSeconds(61));
            await using var scopeB = _fixture.CreateAsyncScope();
            var serviceB = new CheckoutService(
                scopeB.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scopeB.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scopeB.ServiceProvider.GetRequiredService<ILogger<CheckoutService>>(),
                timeProvider);

            var resultB = await serviceB.CheckoutAsync(store.Id, customerId, idempotencyKey, cmd, CancellationToken.None);
            Assert.Equal(CheckoutOutcomeStatus.Created, resultB.Status);
            Assert.NotNull(resultB.Order);

            // 3. Worker A resumes and attempts finalization -> fenced out, returns Replayed of Worker B's Order #1
            hookA.Release();
            var resultA = await taskA;
            Assert.Equal(CheckoutOutcomeStatus.Replayed, resultA.Status);
            Assert.Equal(resultB.Order!.Id, resultA.Order!.Id);

            // 4. Verify SQL Server state: 1 Order, Reserved == 2, Cart empty, CheckoutRequest Completed
            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(1, await verifyDb.Orders.CountAsync(o => o.StoreId == store.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(2, inv.Reserved);
            Assert.Equal(0, await verifyDb.CartItems.CountAsync(ci => ci.StoreId == store.Id));
            var claim = await verifyDb.CheckoutRequests.AsNoTracking().SingleAsync(r => r.StoreId == store.Id && r.IdempotencyKey == idempotencyKey);
            Assert.Equal(CheckoutRequestStatus.Completed, claim.Status);
            Assert.Equal(resultB.Order.Id, claim.OrderId);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task ScenarioB_StaleWorkerAttemptsCompletionWhileWorkerBStillInProgress_RejectedWithoutOverwritingClaim()
    {
        var (store, prod, customerId) = await SeedStoreWithCartAsync(onHand: 10, cartQty: 2);
        try
        {
            var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var hookA = new BarrierFinalizationHook();
            var hookB = new BarrierFinalizationHook();
            var idempotencyKey = $"fence-b-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scopeA = _fixture.CreateAsyncScope();
            var serviceA = new CheckoutService(
                scopeA.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scopeA.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scopeA.ServiceProvider.GetRequiredService<ILogger<CheckoutService>>(),
                timeProvider,
                hookA);

            var taskA = serviceA.CheckoutAsync(store.Id, customerId, idempotencyKey, cmd, CancellationToken.None);
            await hookA.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            timeProvider.Advance(TimeSpan.FromSeconds(61));
            await using var scopeB = _fixture.CreateAsyncScope();
            var serviceB = new CheckoutService(
                scopeB.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scopeB.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scopeB.ServiceProvider.GetRequiredService<ILogger<CheckoutService>>(),
                timeProvider,
                hookB);

            var taskB = serviceB.CheckoutAsync(store.Id, customerId, idempotencyKey, cmd, CancellationToken.None);
            await hookB.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            // Capture Worker B's ClaimToken while Worker B is still InProgress
            Guid tokenB;
            await using (var midDb = _fixture.CreateDbContext())
            {
                tokenB = await midDb.CheckoutRequests
                    .Where(r => r.StoreId == store.Id && r.IdempotencyKey == idempotencyKey)
                    .Select(r => r.ClaimToken)
                    .SingleAsync();
            }

            // Worker A resumes while Worker B is still processing -> must fail cleanly without overwriting TokenB
            hookA.Release();
            var resultA = await taskA;
            Assert.Equal(CheckoutOutcomeStatus.ConcurrentInFlight, resultA.Status);

            await using (var midDb2 = _fixture.CreateDbContext())
            {
                var claimMid = await midDb2.CheckoutRequests.AsNoTracking()
                    .SingleAsync(r => r.StoreId == store.Id && r.IdempotencyKey == idempotencyKey);
                Assert.Equal(CheckoutRequestStatus.InProgress, claimMid.Status);
                Assert.Equal(tokenB, claimMid.ClaimToken);
                Assert.Equal(0, await midDb2.Orders.CountAsync(o => o.StoreId == store.Id));
            }

            // Now Worker B completes cleanly
            hookB.Release();
            var resultB = await taskB;
            Assert.Equal(CheckoutOutcomeStatus.Created, resultB.Status);

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
    public async Task ScenarioC_ExpiredLeaseWithoutReclaim_OwnerRejectedAtCommitTime()
    {
        var (store, prod, customerId) = await SeedStoreWithCartAsync(onHand: 10, cartQty: 2);
        try
        {
            var timeProvider = new MutableTimeProvider(DateTimeOffset.UtcNow);
            var hook = new BarrierFinalizationHook();
            var idempotencyKey = $"fence-c-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scope = _fixture.CreateAsyncScope();
            var service = new CheckoutService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<CheckoutService>>(),
                timeProvider,
                hook);

            var task = service.CheckoutAsync(store.Id, customerId, idempotencyKey, cmd, CancellationToken.None);
            await hook.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            // Lease expires with NO competing worker reclaiming it
            timeProvider.Advance(TimeSpan.FromSeconds(61));
            hook.Release();

            var result = await task;
            Assert.Equal(CheckoutOutcomeStatus.ConcurrentInFlight, result.Status);

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(0, await verifyDb.Orders.CountAsync(o => o.StoreId == store.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(0, inv.Reserved);
            Assert.Equal(1, await verifyDb.CartItems.CountAsync(ci => ci.StoreId == store.Id));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task ScenarioDAndE_SaveChangesFailureRollsBackTransaction_And_CompetingRequestsCreateSingleOrder()
    {
        // Scenario D: RowVersion conflict during final SaveChanges rolls back CheckoutRequest + Order + Inventory + Cart
        var (storeD, prodD, customerD) = await SeedStoreWithCartAsync(onHand: 10, cartQty: 2);
        try
        {
            var hook = new BarrierFinalizationHook();
            var idempotencyKey = $"fence-d-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scope = _fixture.CreateAsyncScope();
            var service = new CheckoutService(
                scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>(),
                scope.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                scope.ServiceProvider.GetRequiredService<ILogger<CheckoutService>>(),
                TimeProvider.System,
                hook);

            var task = service.CheckoutAsync(storeD.Id, customerD, idempotencyKey, cmd, CancellationToken.None);
            await hook.Entered.WaitAsync(TimeSpan.FromSeconds(10));

            // Mutate InventoryItem concurrently to change its SQL RowVersion before SaveChangesAsync
            await using (var competingDb = _fixture.CreateDbContext())
            {
                await competingDb.InventoryItems
                    .Where(i => i.ProductId == prodD.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, 9));
            }

            hook.Release();
            var result = await task;
            Assert.Equal(CheckoutOutcomeStatus.ConcurrencyConflict, result.Status);

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(0, await verifyDb.Orders.CountAsync(o => o.StoreId == storeD.Id));
            Assert.Equal(0, await verifyDb.OrderItems.CountAsync(oi => oi.StoreId == storeD.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodD.Id);
            Assert.Equal(0, inv.Reserved);
            Assert.Equal(1, await verifyDb.CartItems.CountAsync(ci => ci.StoreId == storeD.Id));
            var claim = await verifyDb.CheckoutRequests.AsNoTracking().SingleAsync(r => r.StoreId == storeD.Id && r.IdempotencyKey == idempotencyKey);
            Assert.Equal(CheckoutRequestStatus.Failed, claim.Status);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeD.Id);
        }

        // Scenario E: Competing concurrent requests with the same Idempotency-Key
        var (storeE, prodE, customerE) = await SeedStoreWithCartAsync(onHand: 10, cartQty: 2);
        try
        {
            var idempotencyKey = $"fence-e-{Guid.NewGuid():N}";
            var cmd = new CheckoutCommand(FulfillmentMode.Pickup, null, null);

            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var s1 = scope1.ServiceProvider.GetRequiredService<ICheckoutService>();
            var s2 = scope2.ServiceProvider.GetRequiredService<ICheckoutService>();

            var results = await Task.WhenAll(
                s1.CheckoutAsync(storeE.Id, customerE, idempotencyKey, cmd, CancellationToken.None),
                s2.CheckoutAsync(storeE.Id, customerE, idempotencyKey, cmd, CancellationToken.None));

            Assert.Equal(1, results.Count(r => r.Status == CheckoutOutcomeStatus.Created));
            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Equal(1, await verifyDb.Orders.CountAsync(o => o.StoreId == storeE.Id));
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodE.Id);
            Assert.Equal(2, inv.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeE.Id);
        }
    }

    [Fact]
    public async Task UnscopedCookieValidation_PublicRoutesPreserveSession_SecurityStampRevokes_And_UnscopedProtectedFailsClosed()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var storeA = Store.Create(Guid.NewGuid(), "Cookie A", $"cookiea-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var storeB = Store.Create(Guid.NewGuid(), "Cookie B", $"cookieb-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var settingsA = StoreSettings.Create(Guid.NewGuid(), storeA, Jod, "Asia/Amman", true, false);
        var settingsB = StoreSettings.Create(Guid.NewGuid(), storeB, Jod, "Asia/Amman", true, false);
        var adminId = Guid.NewGuid();
        var adminEmail = $"cookieadmin-{suffix}@retailflow.test";

        await using (var scope = _fixture.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            await db.SaveChangesAsync();

            var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
            scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<RetailFlowIdentityRole>>();
            if (!await roleManager.RoleExistsAsync(IdentityRoleNames.PlatformAdmin))
            {
                await roleManager.CreateAsync(new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.PlatformAdminRoleId,
                    Name = IdentityRoleNames.PlatformAdmin,
                    NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                });
            }

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
            var admin = new RetailFlowIdentityUser
            {
                Id = adminId,
                UserName = adminId.ToString("N"),
                Email = adminEmail,
                DisplayName = "Cookie Admin",
                StoreId = null,
                IsActive = true,
                CreatedAtUtc = now
            };
            Assert.True((await userManager.CreateAsync(admin, "Adm1nPassphrase!99")).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin)).Succeeded);
        }

        try
        {
            var email = $"cookie-{suffix}@example.com";
            using var client = _fixture.CreateHttpsClient();
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var reg = await client.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/register",
                new RegisterCustomerCommand(email, "Cookie User", "Str0ngPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
            var userDto = await reg.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            Assert.NotNull(userDto);

            // Tests A, B, C: Authenticated cookie on /health/live, /health/ready, /openapi/v1.json, and / (SPA/static fallback)
            using var live = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, live.StatusCode);

            using var ready = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

            using var openApi = await client.GetAsync("/openapi/v1.json");
            Assert.Equal(HttpStatusCode.OK, openApi.StatusCode);

            using var rootStatic = await client.GetAsync("/");
            Assert.NotEqual(HttpStatusCode.InternalServerError, rootStatic.StatusCode);

            // SessionTicket remains intact in SQL Server
            await using (var db = _fixture.CreateDbContext())
            {
                Assert.True(await db.SessionTickets.AnyAsync(t => t.UserId == userDto!.UserId));
            }

            // Test D: Subsequent Store A /auth/me still works
            using var meAfterPublic = await client.GetAsync($"/api/stores/{storeA.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.OK, meAfterPublic.StatusCode);

            // Test F: Cross-store Store A cookie on Store B returns 403 without destroying Store A session
            using var crossMe = await client.GetAsync($"/api/stores/{storeB.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Forbidden, crossMe.StatusCode);

            using var stillValidInA = await client.GetAsync($"/api/stores/{storeA.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.OK, stillValidInA.StatusCode);

            // Test G: PlatformAdmin cookie on /health/live and /health/ready works without scope failure
            using var adminClient = _fixture.CreateHttpsClient();
            adminClient.DefaultRequestHeaders.Add("X-CSRF", "1");
            using var adminLogin = await adminClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(adminEmail, "Adm1nPassphrase!99"));
            Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/health/live")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/health/ready")).StatusCode);

            // Test E: SecurityStamp update revokes protected Store A session
            await using (var scope = _fixture.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(storeA.Id));
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
                var user = await userManager.FindByIdAsync(userDto!.UserId.ToString());
                Assert.NotNull(user);
                await userManager.UpdateSecurityStampAsync(user!);
            }

            using var revokedMe = await client.GetAsync($"/api/stores/{storeA.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, revokedMe.StatusCode);

            // Test H: Protected endpoint mapped without Store or Platform scope fails closed (401, never unscoped lookup)
            await using var unscopedFactory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Development");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                            ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString
                        });
                    });
                    builder.ConfigureServices(services =>
                    {
                        services.AddTransient<IStartupFilter>(_ => new UnscopedProtectedEndpointStartupFilter());
                    });
                });

            using var unscopedClient = unscopedFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            using var unscopedLogin = await unscopedClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(adminEmail, "Adm1nPassphrase!99"));
            Assert.Equal(HttpStatusCode.OK, unscopedLogin.StatusCode);

            using var unscopedProtected = await unscopedClient.GetAsync("/test-unscoped-protected");
            Assert.Equal(HttpStatusCode.Unauthorized, unscopedProtected.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }
}
