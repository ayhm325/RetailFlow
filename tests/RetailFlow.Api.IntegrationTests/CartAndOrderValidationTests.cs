using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class CartAndOrderValidationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public CartAndOrderValidationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(Guid AdminId, Guid StoreId, string StoreSlug, Guid ProductId, HttpClient MerchantClient, HttpClient CustomerClient)> SetupStoreMerchantAndCustomerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var adminEmail = $"admin-val-{suffix}@retailflow.test";

        await using (var scope = _fixture.CreateAsyncScope())
        {
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
            new CreateStoreCommand("Val Shop", $"val-shop-{suffix}", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false, null, "Dock"));
        Assert.Equal(HttpStatusCode.Created, createStoreResp.StatusCode);
        var store = (await createStoreResp.Content.ReadFromJsonAsync<PlatformStoreDto>(JsonOptions))!;

        using var approveResp = await adminClient.PostAsync($"/api/platform/stores/{store.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approveResp.StatusCode);

        var merchantEmail = $"merch-val-{suffix}@example.com";
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
            new CreateProductCommand(category.Id, "Beans", "Single origin", $"SKU-{suffix}", 10.000m, ProductStatus.Published, 20));
        Assert.Equal(HttpStatusCode.Created, prodResp.StatusCode);
        var product = (await prodResp.Content.ReadFromJsonAsync<MerchantProductDto>(JsonOptions))!;

        var customerClient = _fixture.CreateHttpsClient();
        customerClient.DefaultRequestHeaders.Add("X-CSRF", "1");
        using var custReg = await customerClient.PostAsJsonAsync(
            $"/api/stores/{store.Slug}/auth/register",
            new RegisterCustomerCommand($"cust-val-{suffix}@example.com", "Customer", "CustPassphrase!1"));
        Assert.Equal(HttpStatusCode.Created, custReg.StatusCode);

        return (adminId, store.Id, store.Slug, product.Id, merchantClient, customerClient);
    }

    private static async Task AssertValidationProblemAsync(
        HttpResponseMessage response,
        string expectedField,
        string expectedInstance)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        Assert.Equal("about:blank", root.GetProperty("type").GetString());
        Assert.Equal("Validation Failed", root.GetProperty("title").GetString());
        Assert.Equal(400, root.GetProperty("status").GetInt32());
        Assert.Equal("validation_error", root.GetProperty("code").GetString());
        Assert.Equal(expectedInstance, root.GetProperty("instance").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
        Assert.True(root.GetProperty("errors").TryGetProperty(expectedField, out var fieldErrors));
        Assert.True(fieldErrors.GetArrayLength() > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CartUpsert_ZeroOrNegativeQuantity_Returns400ValidationError_WithoutMutatingCartOrRowVersion(int invalidQuantity)
    {
        var (adminId, storeId, slug, productId, merchantClient, customerClient) = await SetupStoreMerchantAndCustomerAsync();

        try
        {
            var path = $"/api/stores/{slug}/cart/items";

            // Seed existing cart with 1 valid item so we also verify existing Cart RowVersion is untouched
            using (var seedResp = await customerClient.PutAsJsonAsync(path, new UpsertCartItemCommand(productId, 2)))
            {
                Assert.Equal(HttpStatusCode.OK, seedResp.StatusCode);
            }

            byte[] initialRowVersion;
            int initialItemCount;
            await using (var db = _fixture.CreateDbContext())
            {
                var cart = await db.Carts.AsNoTracking().Include(c => c.Items).SingleAsync(c => c.StoreId == storeId);
                initialRowVersion = cart.RowVersion;
                initialItemCount = cart.Items.Count;
            }

            using var resp = await customerClient.PutAsJsonAsync(path, new UpsertCartItemCommand(productId, invalidQuantity));
            await AssertValidationProblemAsync(resp, "Quantity", path);

            await using var verifyDb = _fixture.CreateDbContext();
            var reloadedCart = await verifyDb.Carts.AsNoTracking().Include(c => c.Items).SingleAsync(c => c.StoreId == storeId);
            Assert.Equal(initialRowVersion, reloadedCart.RowVersion);
            Assert.Equal(initialItemCount, reloadedCart.Items.Count);
            Assert.Equal(2, reloadedCart.Items.Single().Quantity);
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
    public async Task CartUpsert_UnavailableOrCrossStoreProducts_AllReturnUniform404ProductUnavailable()
    {
        var (adminId, storeAId, slugA, _, merchantClientA, customerClientA) = await SetupStoreMerchantAndCustomerAsync();

        // Seed Draft, Archived, Inactive-Category products in Store A, plus a Published product in Store B
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var storeB = Store.Create(Guid.NewGuid(), "Other Store", $"other-val-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settingsB = StoreSettings.Create(Guid.NewGuid(), storeB, Jod, "Asia/Amman", true, false, null, "Dock B");
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "Cat B", "cat-b", true, 1);
        var prodB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Secret StoreB Product", 15.000m, settingsB, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);

        Guid draftId, archivedId, inactiveCatProdId;
        await using (var db = _fixture.CreateDbContext())
        {
            var settingsA = await db.StoreSettings.SingleAsync(s => s.StoreId == storeAId);
            var activeCatA = await db.Categories.SingleAsync(c => c.StoreId == storeAId);
            var inactiveCatA = Category.Create(Guid.NewGuid(), storeAId, "Inactive Cat", "inactive-cat", false, 2);

            var draftProd = Product.Create(Guid.NewGuid(), storeAId, activeCatA.Id, "Draft Prod", 5.000m, settingsA, Jod, ProductStatus.Draft, DateTimeOffset.UtcNow);
            var archivedProd = Product.Create(Guid.NewGuid(), storeAId, activeCatA.Id, "Archived Prod", 5.000m, settingsA, Jod, ProductStatus.Archived, DateTimeOffset.UtcNow);
            var inactiveCatProd = Product.Create(Guid.NewGuid(), storeAId, inactiveCatA.Id, "Hidden Cat Prod", 5.000m, settingsA, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);

            draftId = draftProd.Id;
            archivedId = archivedProd.Id;
            inactiveCatProdId = inactiveCatProd.Id;

            db.Categories.Add(inactiveCatA);
            db.Products.AddRange(draftProd, archivedProd, inactiveCatProd);
            db.Stores.Add(storeB);
            db.StoreSettings.Add(settingsB);
            db.Categories.Add(catB);
            db.Products.Add(prodB);
            await db.SaveChangesAsync();
        }

        try
        {
            var path = $"/api/stores/{slugA}/cart/items";
            var unavailableProductIds = new[]
            {
                Guid.NewGuid(),     // 1. Nonexistent
                prodB.Id,           // 2. Belongs to Store B
                draftId,            // 3. Draft
                archivedId,         // 4. Archived
                inactiveCatProdId   // 5. Inactive category
            };

            string? expectedTitle = null;
            string? expectedDetail = null;

            foreach (var targetProductId in unavailableProductIds)
            {
                using var resp = await customerClientA.PutAsJsonAsync(path, new UpsertCartItemCommand(targetProductId, 1));
                Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
                Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

                var body = await resp.Content.ReadAsStringAsync();
                Assert.DoesNotContain(targetProductId.ToString(), body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(storeB.Id.ToString(), body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(storeB.Slug, body, StringComparison.OrdinalIgnoreCase);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                Assert.Equal("about:blank", root.GetProperty("type").GetString());
                Assert.Equal(404, root.GetProperty("status").GetInt32());
                Assert.Equal("product_unavailable", root.GetProperty("code").GetString());
                Assert.Equal(path, root.GetProperty("instance").GetString());
                Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

                var title = root.GetProperty("title").GetString();
                var detail = root.GetProperty("detail").GetString();
                expectedTitle ??= title;
                expectedDetail ??= detail;
                Assert.Equal(expectedTitle, title);
                Assert.Equal(expectedDetail, detail);
            }

            await using var verifyDb = _fixture.CreateDbContext();
            Assert.Empty(await verifyDb.CartItems.Where(ci => ci.StoreId == storeAId || ci.StoreId == storeB.Id).ToListAsync());
            var reloadedProdB = await verifyDb.Products.AsNoTracking().SingleAsync(p => p.Id == prodB.Id);
            Assert.Equal(ProductStatus.Published, reloadedProdB.Status);
        }
        finally
        {
            merchantClientA.Dispose();
            customerClientA.Dispose();
            await _fixture.DeleteStoresAsync(storeAId, storeB.Id);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task MerchantReject_InvalidReasonsReturn400_ValidReasonReturns200_AndPrecedenceEnforced()
    {
        var (adminId, storeId, slug, productId, merchantClient, customerClient) = await SetupStoreMerchantAndCustomerAsync();

        try
        {
            // Create a Pending order via Cart + Checkout
            using (var cartResp = await customerClient.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productId, 1)))
                Assert.Equal(HttpStatusCode.OK, cartResp.StatusCode);

            var checkoutReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout");
            checkoutReq.Headers.Add("Idempotency-Key", $"idem-val-{Guid.NewGuid():N}");
            checkoutReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, null, "Test St 1"));
            // OnlineOnly store requires DeliveryZone; create one first
            using var zoneResp = await merchantClient.PostAsJsonAsync($"/api/stores/{slug}/merchant/delivery-zones", new CreateDeliveryZoneCommand("Zone 1", 2.000m, true));
            Assert.Equal(HttpStatusCode.Created, zoneResp.StatusCode);
            var zone = (await zoneResp.Content.ReadFromJsonAsync<MerchantDeliveryZoneDto>(JsonOptions))!;

            checkoutReq.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone.Id, "Test St 1"));
            using var checkoutResp = await customerClient.SendAsync(checkoutReq);
            Assert.Equal(HttpStatusCode.Created, checkoutResp.StatusCode);
            var order = (await checkoutResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;

            var rejectPath = $"/api/stores/{slug}/merchant/orders/{order.Id}/reject";
            var invalidPayloads = new[]
            {
                "{}",
                "{\"reason\":null}",
                "{\"reason\":\"\"}",
                "{\"reason\":\"   \"}"
            };

            foreach (var payload in invalidPayloads)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, rejectPath)
                {
                    Content = new StringContent(payload, Encoding.UTF8, "application/json")
                };
                using var resp = await merchantClient.SendAsync(req);
                await AssertValidationProblemAsync(resp, "Reason", rejectPath);
            }

            // Order must still be Pending with only the creation history record
            await using (var db = _fixture.CreateDbContext())
            {
                var reloaded = await db.Orders.AsNoTracking().Include(o => o.History).SingleAsync(o => o.Id == order.Id);
                Assert.Equal(OrderStatus.Pending, reloaded.OrderStatus);
                Assert.Single(reloaded.History);
            }

            // Precedence: invalid reason + nonexistent order ID => 400 (NOT 404)
            var missingOrderPath = $"/api/stores/{slug}/merchant/orders/{Guid.NewGuid()}/reject";
            using (var invalidMissingResp = await merchantClient.PostAsJsonAsync(missingOrderPath, new { Reason = "   " }))
            {
                await AssertValidationProblemAsync(invalidMissingResp, "Reason", missingOrderPath);
            }

            // Valid reason + nonexistent order ID => 404
            using (var validMissingResp = await merchantClient.PostAsJsonAsync(missingOrderPath, new { Reason = "Out of stock" }))
            {
                Assert.Equal(HttpStatusCode.NotFound, validMissingResp.StatusCode);
            }

            // Confirm the order to test Confirmed precedence, after creating a second order for valid-reject test
            using (var cart2 = await customerClient.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productId, 1)))
                Assert.Equal(HttpStatusCode.OK, cart2.StatusCode);
            var checkout2Req = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout");
            checkout2Req.Headers.Add("Idempotency-Key", $"idem-val2-{Guid.NewGuid():N}");
            checkout2Req.Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, zone.Id, "Test St 2"));
            using var checkout2Resp = await customerClient.SendAsync(checkout2Req);
            Assert.Equal(HttpStatusCode.Created, checkout2Resp.StatusCode);
            var order2 = (await checkout2Resp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;

            // Valid reason on Pending order2 => 200 OK
            using (var validRejectResp = await merchantClient.PostAsJsonAsync(
                $"/api/stores/{slug}/merchant/orders/{order2.Id}/reject",
                new { Reason = "Out of stock" }))
            {
                Assert.Equal(HttpStatusCode.OK, validRejectResp.StatusCode);
                var rejected = (await validRejectResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;
                Assert.Equal(OrderStatus.Rejected, rejected.OrderStatus);
            }

            // Confirm order1
            using (var confirmResp = await merchantClient.PostAsync($"/api/stores/{slug}/merchant/orders/{order.Id}/confirm", null))
                Assert.Equal(HttpStatusCode.OK, confirmResp.StatusCode);

            // Precedence: invalid reason + Confirmed order => 400 (NOT 409)
            using (var invalidConfirmedResp = await merchantClient.PostAsJsonAsync(rejectPath, new { Reason = "" }))
            {
                await AssertValidationProblemAsync(invalidConfirmedResp, "Reason", rejectPath);
            }

            // Precedence: valid reason + Confirmed order => 409 order_state_conflict
            using (var validConfirmedResp = await merchantClient.PostAsJsonAsync(rejectPath, new { Reason = "Too late" }))
            {
                Assert.Equal(HttpStatusCode.Conflict, validConfirmedResp.StatusCode);
                using var doc = JsonDocument.Parse(await validConfirmedResp.Content.ReadAsStringAsync());
                Assert.Equal("order_state_conflict", doc.RootElement.GetProperty("code").GetString());
            }
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
