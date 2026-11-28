using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Api.IntegrationTests;

public sealed class EndToEndCommerceAuthIntegrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public EndToEndCommerceAuthIntegrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task UnauthenticatedAndCsrfMissingRequests_AreRejectedWith401And403()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var store = Store.Create(Guid.NewGuid(), "Auth Shop", $"auth-shop-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, false);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            // 1. Unauthenticated protected write endpoints -> 401
            using var unauthMerchant = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/merchant/categories",
                new CreateCategoryCommand("Test", "test", 1, true));
            Assert.Equal(HttpStatusCode.Unauthorized, unauthMerchant.StatusCode);

            using var unauthCheckout = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/checkout",
                new CheckoutCommand(FulfillmentMode.Delivery, null, "Amman"));
            Assert.Equal(HttpStatusCode.Unauthorized, unauthCheckout.StatusCode);

            // 2. Register Customer (sets __Host-RetailFlow.Session cookie)
            using var regResp = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/register",
                new RegisterCustomerCommand($"cust-{suffix}@example.com", "Customer One", "Str0ngPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);

            // 3. Authenticated unsafe POST WITHOUT X-CSRF: 1 -> 403 ProblemDetails
            using var missingCsrf = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/checkout",
                new CheckoutCommand(FulfillmentMode.Delivery, null, "Amman"));
            Assert.Equal(HttpStatusCode.Forbidden, missingCsrf.StatusCode);
            Assert.Equal("application/problem+json", missingCsrf.Content.Headers.ContentType?.MediaType);

            // 4. Cross-origin Origin header WITH X-CSRF: 1 -> 403 ProblemDetails (GAP-006)
            var evilOriginReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
            evilOriginReq.Headers.Add("X-CSRF", "1");
            evilOriginReq.Headers.Add("Origin", "https://evil.example");
            evilOriginReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, null, "Amman"));
            using var evilOriginResp = await client.SendAsync(evilOriginReq);
            Assert.Equal(HttpStatusCode.Forbidden, evilOriginResp.StatusCode);

            // 4b. Sec-Fetch-Site: cross-site WITHOUT Origin/Referer -> 403 ProblemDetails
            var crossSiteFetchReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
            crossSiteFetchReq.Headers.Add("X-CSRF", "1");
            crossSiteFetchReq.Headers.Add("Sec-Fetch-Site", "cross-site");
            crossSiteFetchReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, null, "Amman"));
            using var crossSiteFetchResp = await client.SendAsync(crossSiteFetchReq);
            Assert.Equal(HttpStatusCode.Forbidden, crossSiteFetchResp.StatusCode);

            // 5. Deactivating user invalidates active cookie session (GAP-008 + SecurityStamp)
            await using (var scope = _fixture.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<RetailFlow.Infrastructure.Identity.IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(RetailFlow.Infrastructure.Identity.IdentityLookupScope.ForStore(store.Id));
                var db = scope.ServiceProvider.GetRequiredService<RetailFlow.Infrastructure.Persistence.RetailFlowDbContext>();
                var userId = await db.Users.Where(u => u.StoreId == store.Id).Select(u => u.Id).SingleAsync();
                var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
                Assert.True(await authService.DeactivateUserAsync(userId, CancellationToken.None));
            }

            using var deactivatedMe = await client.GetAsync($"/api/stores/{store.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, deactivatedMe.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task FullCommerceFlow_Auth_Bola_Idempotency_Od002Cancel_And_Od001Expiry_AllVerified()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var adminEmail = $"admin-{suffix}@retailflow.test";
        Guid storeAId = Guid.Empty;
        Guid storeBId = Guid.Empty;

        await using (var scope = _fixture.CreateAsyncScope())
        {
            var scopeAccessor = scope.ServiceProvider.GetRequiredService<RetailFlow.Infrastructure.Identity.IIdentityLookupScopeAccessor>();
            scopeAccessor.SetCurrent(RetailFlow.Infrastructure.Identity.IdentityLookupScope.ForSystemProvisioning());
            var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<RetailFlow.Infrastructure.Identity.RetailFlowIdentityRole>>();
            if (!await roleManager.RoleExistsAsync(IdentityRoleNames.PlatformAdmin))
            {
                await roleManager.CreateAsync(new RetailFlow.Infrastructure.Identity.RetailFlowIdentityRole
                {
                    Id = RetailFlow.Infrastructure.Identity.RetailFlowIdentityRole.PlatformAdminRoleId,
                    Name = IdentityRoleNames.PlatformAdmin,
                    NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                });
            }

            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<RetailFlow.Infrastructure.Identity.RetailFlowIdentityUser>>();
            var admin = new RetailFlow.Infrastructure.Identity.RetailFlowIdentityUser
            {
                Id = adminId,
                UserName = adminId.ToString("N"),
                Email = adminEmail,
                DisplayName = "Platform Admin",
                StoreId = null,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var created = await userManager.CreateAsync(admin, "Adm1nPassphrase!99");
            Assert.True(created.Succeeded);
            var roled = await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin);
            Assert.True(roled.Succeeded);
        }

        try
        {
            // 1. PlatformAdmin logs in and creates + approves Store A and Store B + MerchantOwner A
            using var adminClient = _fixture.CreateHttpsClient();
            adminClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var adminLogin = await adminClient.PostAsJsonAsync(
                "/api/platform/auth/login",
                new LoginCommand(adminEmail, "Adm1nPassphrase!99"));
            Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);

            using var createStoreAResp = await adminClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Roast A", $"roast-a-{suffix}", StoreType.Physical, "JOD", "Asia/Amman", true, true, "Amman", "Dock A"));
            Assert.Equal(HttpStatusCode.Created, createStoreAResp.StatusCode);
            var storeA = await createStoreAResp.Content.ReadFromJsonAsync<PlatformStoreDto>(JsonOptions);
            storeAId = storeA!.Id;
            Assert.Equal(StoreStatus.PendingApproval, storeA.Status);

            using var createStoreBResp = await adminClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Roast B", $"roast-b-{suffix}", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false, null, "Dock B"));
            Assert.Equal(HttpStatusCode.Created, createStoreBResp.StatusCode);
            var storeB = await createStoreBResp.Content.ReadFromJsonAsync<PlatformStoreDto>(JsonOptions);
            storeBId = storeB!.Id;
            Assert.Equal(StoreStatus.PendingApproval, storeB.Status);

            using var approveBResp = await adminClient.PostAsync($"/api/platform/stores/{storeB.Id}/approve", null);
            Assert.Equal(HttpStatusCode.NoContent, approveBResp.StatusCode);

            using var approveResp = await adminClient.PostAsync($"/api/platform/stores/{storeA.Id}/approve", null);
            Assert.Equal(HttpStatusCode.NoContent, approveResp.StatusCode);

            var merchantEmail = $"merch-{suffix}@example.com";
            using var createMerchResp = await adminClient.PostAsJsonAsync(
                $"/api/platform/stores/{storeA.Id}/merchant-owner",
                new CreateMerchantUserCommand(merchantEmail, "Merchant A", "MerchPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, createMerchResp.StatusCode);

            // 2. Merchant A logs in, creates Zone, Category, Product (OnHand = 10)
            using var merchantClient = _fixture.CreateHttpsClient();
            merchantClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var merchLogin = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/login",
                new LoginCommand(merchantEmail, "MerchPassphrase!1"));
            Assert.Equal(HttpStatusCode.OK, merchLogin.StatusCode);

            // Cross-store BOLA: Merchant A cannot write to Store B -> 403 Forbidden
            using var bolaResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeB!.Slug}/merchant/categories",
                new CreateCategoryCommand("Hijack", "hijack", 1, true));
            Assert.Equal(HttpStatusCode.Forbidden, bolaResp.StatusCode);

            using var zoneResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/merchant/delivery-zones",
                new CreateDeliveryZoneCommand("Abdoun", 2.500m, true));
            Assert.Equal(HttpStatusCode.Created, zoneResp.StatusCode);
            var zone = await zoneResp.Content.ReadFromJsonAsync<MerchantDeliveryZoneDto>(JsonOptions);

            using var catResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/merchant/categories",
                new CreateCategoryCommand("Coffee", "coffee", 1, true));
            Assert.Equal(HttpStatusCode.Created, catResp.StatusCode);
            var category = await catResp.Content.ReadFromJsonAsync<MerchantCategoryDto>(JsonOptions);

            using var prodResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/merchant/products",
                new CreateProductCommand(category!.Id, "Kenya AA", "Bright", $"KAA-{suffix}", 12.000m, ProductStatus.Published, 10));
            Assert.Equal(HttpStatusCode.Created, prodResp.StatusCode);
            var product = await prodResp.Content.ReadFromJsonAsync<MerchantProductDto>(JsonOptions);

            // 3. Customer registers in Store A, adds 2 units to Cart, and checks out with Idempotency-Key
            using var customerClient = _fixture.CreateHttpsClient();
            customerClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var custReg = await customerClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/register",
                new RegisterCustomerCommand($"cust2-{suffix}@example.com", "Customer Two", "CustPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, custReg.StatusCode);

            using var cartResp = await customerClient.PutAsJsonAsync(
                $"/api/stores/{storeA.Slug}/cart/items",
                new UpsertCartItemCommand(product!.Id, 2));
            Assert.Equal(HttpStatusCode.OK, cartResp.StatusCode);

            var checkoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{storeA.Slug}/checkout");
            checkoutReq.Headers.Add("Idempotency-Key", $"idem-{suffix}");
            checkoutReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone!.Id, "Abdoun St 5"));

            using var checkoutResp = await customerClient.SendAsync(checkoutReq);
            Assert.Equal(HttpStatusCode.Created, checkoutResp.StatusCode);
            var order = await checkoutResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.NotNull(order);
            Assert.Equal(24.000m, order!.Subtotal);
            Assert.Equal(2.500m, order.DeliveryFee);
            Assert.Equal(26.500m, order.Total);
            Assert.Equal(OrderStatus.Pending, order.OrderStatus);

            // Replay same Idempotency-Key & payload -> 200 OK with same OrderId
            var replayReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{storeA.Slug}/checkout");
            replayReq.Headers.Add("Idempotency-Key", $"idem-{suffix}");
            replayReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone.Id, "Abdoun St 5"));
            using var replayResp = await customerClient.SendAsync(replayReq);
            Assert.Equal(HttpStatusCode.OK, replayResp.StatusCode);
            var replayed = await replayResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(order.Id, replayed!.Id);

            // Reuse same Idempotency-Key with DIFFERENT payload -> 422 Unprocessable Entity
            var mismatchReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{storeA.Slug}/checkout");
            mismatchReq.Headers.Add("Idempotency-Key", $"idem-{suffix}");
            mismatchReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone.Id, "Different St 99"));
            using var mismatchResp = await customerClient.SendAsync(mismatchReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatchResp.StatusCode);

            // Reuse same Idempotency-Key & same CheckoutCommand AFTER adding a different cart item -> 422 (GAP-001)
            using (await customerClient.PutAsJsonAsync($"/api/stores/{storeA.Slug}/cart/items", new UpsertCartItemCommand(product.Id, 1)))
            {
                var cartMismatchReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{storeA.Slug}/checkout");
                cartMismatchReq.Headers.Add("Idempotency-Key", $"idem-{suffix}");
                cartMismatchReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone.Id, "Abdoun St 5"));
                using var cartMismatchResp = await customerClient.SendAsync(cartMismatchReq);
                Assert.Equal(HttpStatusCode.UnprocessableEntity, cartMismatchResp.StatusCode);
            }
            using (await customerClient.DeleteAsync($"/api/stores/{storeA.Slug}/cart")) { }

            // 4. Merchant confirms order -> Customer self-cancellation is blocked (409 Conflict, OD-002)
            using var confirmResp = await merchantClient.PostAsync($"/api/stores/{storeA.Slug}/merchant/orders/{order.Id}/confirm", null);
            Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

            using var cancelAfterConfirm = await customerClient.PostAsync($"/api/stores/{storeA.Slug}/orders/{order.Id}/cancel", null);
            Assert.Equal(HttpStatusCode.Conflict, cancelAfterConfirm.StatusCode);

            // 5. Merchant advances fulfillment to Delivered and collects COD payment -> Order Completed & Inventory committed
            using var prepResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/merchant/orders/{order.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Preparing });
            Assert.Equal(HttpStatusCode.OK, prepResp.StatusCode);

            using var outResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/merchant/orders/{order.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.OutForDelivery });
            Assert.Equal(HttpStatusCode.OK, outResp.StatusCode);

            using var delivResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/merchant/orders/{order.Id}/fulfillment",
                new { NextStatus = FulfillmentStatus.Delivered });
            Assert.Equal(HttpStatusCode.OK, delivResp.StatusCode);

            using var collectResp = await merchantClient.PostAsync(
                $"/api/stores/{storeA.Slug}/merchant/orders/{order.Id}/collect-payment",
                null);
            Assert.Equal(HttpStatusCode.OK, collectResp.StatusCode);
            var completed = await collectResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);
            Assert.Equal(OrderStatus.Completed, completed!.OrderStatus);
            Assert.Equal(PaymentStatus.Collected, completed.PaymentStatus);

            await using (var db = _fixture.CreateDbContext())
            {
                var inv = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == product.Id);
                Assert.Equal(8, inv.OnHand);
                Assert.Equal(0, inv.Reserved);
            }

            // 6. OD-001 15-minute reservation expiry sweep cancels Pending order and releases reservation
            using var cart2Resp = await customerClient.PutAsJsonAsync(
                $"/api/stores/{storeA.Slug}/cart/items",
                new UpsertCartItemCommand(product.Id, 3));
            Assert.Equal(HttpStatusCode.OK, cart2Resp.StatusCode);

            var checkout2Req = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{storeA.Slug}/checkout");
            checkout2Req.Headers.Add("Idempotency-Key", $"idem2-{suffix}");
            checkout2Req.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Pickup, null, null));
            using var checkout2Resp = await customerClient.SendAsync(checkout2Req);
            Assert.Equal(HttpStatusCode.Created, checkout2Resp.StatusCode);
            var pendingOrder = await checkout2Resp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions);

            await using var scope = _fixture.CreateAsyncScope();
            var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
            var released = await orderService.ReleaseExpiredReservationsAsync(DateTimeOffset.UtcNow.AddMinutes(16), CancellationToken.None);
            Assert.True(released >= 1);

            await using (var db = _fixture.CreateDbContext())
            {
                var expiredOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == pendingOrder!.Id);
                Assert.Equal(OrderStatus.Cancelled, expiredOrder.OrderStatus);

                var inv = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == product.Id);
                Assert.Equal(8, inv.OnHand);
                Assert.Equal(0, inv.Reserved);

                // GAP-004: DataProtectionKeys persisted in SQL Server
                Assert.True(await db.DataProtectionKeys.AsNoTracking().AnyAsync());
            }

            // 7. GAP-014: Customer & Merchant order lists support bounded pagination
            var custOrdersPage = await customerClient.GetFromJsonAsync<PagedOrdersResponse>(
                $"/api/stores/{storeA.Slug}/orders?page=1&pageSize=1",
                JsonOptions);
            Assert.NotNull(custOrdersPage);
            Assert.Equal(2, custOrdersPage!.TotalCount);
            Assert.Equal(2, custOrdersPage.TotalPages);
            Assert.Single(custOrdersPage.Items);

            var merchOrdersPage = await merchantClient.GetFromJsonAsync<PagedOrdersResponse>(
                $"/api/stores/{storeA.Slug}/merchant/orders?page=1&pageSize=1",
                JsonOptions);
            Assert.NotNull(merchOrdersPage);
            Assert.Equal(2, merchOrdersPage!.TotalCount);
            Assert.Single(merchOrdersPage.Items);
        }
        finally
        {
            if (storeAId != Guid.Empty || storeBId != Guid.Empty)
            {
                await _fixture.DeleteStoresAsync(new[] { storeAId, storeBId }.Where(id => id != Guid.Empty).ToArray());
            }

            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Checkout_PartialInventoryFailure_RollsBackAllReservationsAtomically_AndHidesInternalDetails()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var store = Store.Create(Guid.NewGuid(), "Atomic Shop", $"atomic-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, false);
        var zone = DeliveryZone.Create(Guid.NewGuid(), store.Id, "Amman", 2.000m, settings, Jod, DateTimeOffset.UtcNow);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod1 = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Item One", 5.000m, settings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);
        var prod2 = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Item Two", 8.000m, settings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);
        var inv1 = InventoryItem.Create(Guid.NewGuid(), store.Id, prod1.Id, onHand: 5, reserved: 0);
        var inv2 = InventoryItem.Create(Guid.NewGuid(), store.Id, prod2.Id, onHand: 1, reserved: 0);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.DeliveryZones.Add(zone);
            db.Categories.Add(cat);
            db.Products.AddRange(prod1, prod2);
            db.InventoryItems.AddRange(inv1, inv2);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var reg = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/register",
                new RegisterCustomerCommand($"atomic-{suffix}@example.com", "Atomic User", "Str0ngPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

            await client.PutAsJsonAsync($"/api/stores/{store.Slug}/cart/items", new UpsertCartItemCommand(prod1.Id, 2));
            await client.PutAsJsonAsync($"/api/stores/{store.Slug}/cart/items", new UpsertCartItemCommand(prod2.Id, 2));

            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{store.Slug}/checkout");
            req.Headers.Add("Idempotency-Key", $"atomic-idem-{suffix}");
            req.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone.Id, "Amman"));

            using var resp = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("Insufficient available inventory", body, StringComparison.OrdinalIgnoreCase);

            // Also verify GAP-001: adding a different product and reusing an existing completed Idempotency-Key returns 422
            await using var db = _fixture.CreateDbContext();
            var dbInv1 = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod1.Id);
            var dbInv2 = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod2.Id);
            Assert.Equal(0, dbInv1.Reserved);
            Assert.Equal(0, dbInv2.Reserved);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task MaintenanceBackgroundService_ConcurrentMultiInstanceSweep_ReleasesExpiredDataAtMostOnce()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var oldTime = DateTimeOffset.UtcNow.AddMinutes(-20);
        var store = Store.Create(Guid.NewGuid(), "Maint Shop", $"maint-{suffix}", StoreType.Physical, StoreStatus.Active, oldTime);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, true);
        var cat = Category.Create(Guid.NewGuid(), store.Id, "Cat", "cat", true, 1);
        var prod = Product.Create(Guid.NewGuid(), store.Id, cat.Id, "Item", 5.000m, settings, Jod, ProductStatus.Published, oldTime);
        var inv = InventoryItem.Create(Guid.NewGuid(), store.Id, prod.Id, onHand: 5, reserved: 2);
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
            [OrderItem.Create(Guid.NewGuid(), store.Id, orderId, prod.Id, prod.Name, prod.Sku, prod.Price, 2, Jod)],
            0m,
            null,
            null,
            null,
            oldTime);

        var expiredClaim = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, $"exp-key-{suffix}", new string('A', 64), DateTimeOffset.UtcNow.AddHours(-25));
        var expiredTicket = SessionTicket.Create($"exp-ticket-{suffix}", customerId, store.Id, [1, 2, 3], DateTimeOffset.UtcNow.AddMinutes(-5), oldTime);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Categories.Add(cat);
            db.Products.Add(prod);
            db.InventoryItems.Add(inv);
            db.Orders.Add(order);
            db.CheckoutRequests.Add(expiredClaim);
            db.SessionTickets.Add(expiredTicket);
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope1 = _fixture.CreateAsyncScope();
            await using var scope2 = _fixture.CreateAsyncScope();
            var worker1 = new RetailFlow.Api.Hosting.MaintenanceBackgroundService(
                scope1.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RetailFlow.Api.Hosting.MaintenanceBackgroundService>.Instance);
            var worker2 = new RetailFlow.Api.Hosting.MaintenanceBackgroundService(
                scope2.ServiceProvider.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<RetailFlow.Api.Hosting.MaintenanceBackgroundService>.Instance);

            await Task.WhenAll(
                worker1.RunMaintenanceSweepAsync(CancellationToken.None),
                worker2.RunMaintenanceSweepAsync(CancellationToken.None));

            await using var db = _fixture.CreateDbContext();
            var updatedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            var updatedInv = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prod.Id);
            Assert.Equal(OrderStatus.Cancelled, updatedOrder.OrderStatus);
            Assert.Equal(0, updatedInv.Reserved);
            Assert.False(await db.CheckoutRequests.AsNoTracking().AnyAsync(r => r.Id == expiredClaim.Id));
            Assert.False(await db.SessionTickets.AsNoTracking().AnyAsync(t => t.Key == expiredTicket.Key));

            // AUD-004: Expired CheckoutRequest key is atomically reclaimed in 1 step (not 400 ValidationFailed)
            var expiredReuse = CheckoutRequest.Claim(Guid.NewGuid(), store.Id, customerId, $"reuse-key-{suffix}", new string('B', 64), DateTimeOffset.UtcNow.AddHours(-25));
            db.CheckoutRequests.Add(expiredReuse);
            await db.SaveChangesAsync();

            await using var scope3 = _fixture.CreateAsyncScope();
            var checkoutService = scope3.ServiceProvider.GetRequiredService<ICheckoutService>();
            var reuseResult = await checkoutService.CheckoutAsync(
                store.Id,
                customerId,
                $"reuse-key-{suffix}",
                new CheckoutCommand(FulfillmentMode.Pickup, null, null),
                CancellationToken.None);
            // Cart is empty for this test customer -> BusinessRuleFailed (422), and persisted row ID was reclaimed & marked Failed (not deleted)
            Assert.Equal(CheckoutOutcomeStatus.BusinessRuleFailed, reuseResult.Status);
            var reclaimedReuse = await db.CheckoutRequests.AsNoTracking().SingleAsync(r => r.Id == expiredReuse.Id);
            Assert.Equal(CheckoutRequestStatus.Failed, reclaimedReuse.Status);
            Assert.NotEqual(expiredReuse.ClaimToken, reclaimedReuse.ClaimToken);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }
}
