using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

/// <summary>
/// Tier 3 Authorization & Tenant Isolation regression and coverage suite (OWASP API1/API3/API5)
/// backed by real SQL Server and ASP.NET Core Identity.
/// </summary>
public sealed class AuthorizationAndTenantIsolationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public AuthorizationAndTenantIsolationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateProductionFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["Email:RequireConfiguredInProduction"] = "false",
                        ["RateLimiting:PermitLimit"] = "500",
                        ["RateLimiting:AuthPermitLimit"] = "500"
                    };
                    TestDataProtectionCertHelper.ApplyTestCertificate(values);
                    config.AddInMemoryCollection(values);
                });
            });
    }

    private static HttpClient CreateCookieClient(WebApplicationFactory<Program> factory, bool handleCookies = true)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://retail.example.com"),
            HandleCookies = handleCookies,
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-CSRF", "1");
        return client;
    }

    private async Task<(Store Store, Category Category, Product Product, ProductImage Image, DeliveryZone Zone)> SeedStoreWithCatalogAsync(string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var store = Store.Create(Guid.NewGuid(), $"{prefix} Store", $"{prefix}-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, false, null, $"Internal-Dock-{prefix}-{suffix}");
        var zone = DeliveryZone.Create(Guid.NewGuid(), store.Id, $"Zone-{prefix}", 2.500m, settings, Jod, DateTimeOffset.UtcNow);
        var category = Category.Create(Guid.NewGuid(), store.Id, $"Cat-{prefix}", $"cat-{prefix}-{suffix}", true, 1);
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, $"Prod-{prefix}", 15.000m, settings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow, "Desc", $"SKU-{prefix}-{suffix}");
        var inventory = InventoryItem.Create(Guid.NewGuid(), store.Id, product.Id, 20, 0);
        var image = ProductImage.Create(Guid.NewGuid(), store.Id, product.Id, "https://cdn.example.com/item.png", 1, "Alt");

        await using var db = _fixture.CreateDbContext();
        db.Stores.Add(store);
        db.StoreSettings.Add(settings);
        db.DeliveryZones.Add(zone);
        db.Categories.Add(category);
        db.Products.Add(product);
        db.InventoryItems.Add(inventory);
        db.ProductImages.Add(image);
        await db.SaveChangesAsync();

        return (store, category, product, image, zone);
    }

    private async Task<(Guid UserId, string Email)> CreateStoreUserAsync(
        WebApplicationFactory<Program> factory,
        Guid storeId,
        string roleName,
        string password)
    {
        var userId = Guid.NewGuid();
        var email = $"{roleName.ToLowerInvariant()}-{userId:N}@retailflow.test";

        await using var scope = factory.Services.CreateAsyncScope();
        var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
        scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(storeId));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<RetailFlowIdentityRole>>();

        if (!await roleManager.RoleExistsAsync(roleName))
        {
            var role = roleName == IdentityRoleNames.MerchantOwner
                ? new RetailFlowIdentityRole { Id = RetailFlowIdentityRole.MerchantOwnerRoleId, Name = roleName, NormalizedName = roleName.ToUpperInvariant() }
                : new RetailFlowIdentityRole { Id = RetailFlowIdentityRole.CustomerRoleId, Name = roleName, NormalizedName = roleName.ToUpperInvariant() };
            await roleManager.CreateAsync(role);
        }

        var user = new RetailFlowIdentityUser
        {
            Id = userId,
            UserName = userId.ToString("N"),
            Email = email,
            DisplayName = $"{roleName} User",
            StoreId = storeId,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        Assert.True((await userManager.CreateAsync(user, password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(user, roleName)).Succeeded);
        return (userId, email);
    }

    private async Task CleanupUsersAsync(params Guid[] userIds)
    {
        await using var db = _fixture.CreateDbContext();
        await db.SessionTickets.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
        await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task BFLA_RoleFunctionAccessMatrix_DeniesUnauthorizedRolesAcrossBoundaries()
    {
        await using var factory = CreateProductionFactory();
        var s = await SeedStoreWithCatalogAsync("bfla");
        const string pass = "AuthzMatrixPass!2026";
        var (custId, custEmail) = await CreateStoreUserAsync(factory, s.Store.Id, IdentityRoleNames.Customer, pass);
        var (merchId, merchEmail) = await CreateStoreUserAsync(factory, s.Store.Id, IdentityRoleNames.MerchantOwner, pass);

        try
        {
            using var anon = CreateCookieClient(factory, handleCookies: false);
            using var cust = CreateCookieClient(factory);
            using var merch = CreateCookieClient(factory);

            Assert.Equal(HttpStatusCode.OK, (await cust.PostAsJsonAsync($"/api/stores/{s.Store.Slug}/auth/login", new LoginCommand(custEmail, pass))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await merch.PostAsJsonAsync($"/api/stores/{s.Store.Slug}/auth/login", new LoginCommand(merchEmail, pass))).StatusCode);

            // Anonymous -> protected customer, merchant, platform routes => 401
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/stores/{s.Store.Slug}/cart")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/stores/{s.Store.Slug}/merchant/orders")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/api/platform/stores", new CreateStoreCommand("X", "x-slug", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false))).StatusCode);

            // Customer -> Merchant & Platform routes => 403
            Assert.Equal(HttpStatusCode.Forbidden, (await cust.GetAsync($"/api/stores/{s.Store.Slug}/merchant/orders")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cust.PostAsJsonAsync($"/api/stores/{s.Store.Slug}/merchant/categories", new CreateCategoryCommand("Hack", "hack"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await cust.PostAsJsonAsync("/api/platform/stores", new CreateStoreCommand("X", "x-slug", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false))).StatusCode);

            // MerchantOwner -> Customer & Platform routes => 403
            Assert.Equal(HttpStatusCode.Forbidden, (await merch.GetAsync($"/api/stores/{s.Store.Slug}/cart")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await merch.GetAsync($"/api/stores/{s.Store.Slug}/orders")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await merch.PostAsJsonAsync("/api/platform/stores", new CreateStoreCommand("X", "x-slug", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false))).StatusCode);
        }
        finally
        {
            await CleanupUsersAsync(custId, merchId);
            await _fixture.DeleteStoresAsync(s.Store.Id);
        }
    }

    [Fact]
    public async Task BOLA_CustomerOwnership_SameStoreAndCrossStore_OrdersAndCartAreIsolated()
    {
        await using var factory = CreateProductionFactory();
        var sA = await SeedStoreWithCatalogAsync("cust-a");
        var sB = await SeedStoreWithCatalogAsync("cust-b");
        const string pass = "CustOwnership!2026";

        var (custA1Id, custA1Email) = await CreateStoreUserAsync(factory, sA.Store.Id, IdentityRoleNames.Customer, pass);
        var (custA2Id, custA2Email) = await CreateStoreUserAsync(factory, sA.Store.Id, IdentityRoleNames.Customer, pass);
        var (custBId, custBEmail) = await CreateStoreUserAsync(factory, sB.Store.Id, IdentityRoleNames.Customer, pass);

        try
        {
            using var clientA1 = CreateCookieClient(factory);
            using var clientA2 = CreateCookieClient(factory);
            using var clientB = CreateCookieClient(factory);

            Assert.Equal(HttpStatusCode.OK, (await clientA1.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/auth/login", new LoginCommand(custA1Email, pass))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await clientA2.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/auth/login", new LoginCommand(custA2Email, pass))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await clientB.PostAsJsonAsync($"/api/stores/{sB.Store.Slug}/auth/login", new LoginCommand(custBEmail, pass))).StatusCode);

            // Customer A1 creates cart & order in Store A
            Assert.Equal(HttpStatusCode.OK, (await clientA1.PutAsJsonAsync($"/api/stores/{sA.Store.Slug}/cart/items", new UpsertCartItemCommand(sA.Product.Id, 2))).StatusCode);
            using var checkoutMsg = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{sA.Store.Slug}/checkout")
            {
                Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, sA.Zone.Id, "Amman St 1"))
            };
            checkoutMsg.Headers.Add("Idempotency-Key", $"idem-{Guid.NewGuid():N}");
            using var coResp = await clientA1.SendAsync(checkoutMsg);
            Assert.Equal(HttpStatusCode.Created, coResp.StatusCode);
            var orderA1 = (await coResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;

            // Same-store Customer A2 cannot read or cancel Customer A1's order (404, DB untouched)
            Assert.Equal(HttpStatusCode.NotFound, (await clientA2.GetAsync($"/api/stores/{sA.Store.Slug}/orders/{orderA1.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await clientA2.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/orders/{orderA1.Id}/cancel", new { Reason = "Hijack" })).StatusCode);

            // Cross-store Customer B cannot access Store A routes (403)
            Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/stores/{sA.Store.Slug}/cart")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/stores/{sA.Store.Slug}/orders/{orderA1.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await clientB.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/orders/{orderA1.Id}/cancel", new { Reason = "CrossStore" })).StatusCode);

            // Cross-store auth/me & 2fa/setup with Store B cookie on Store A route
            Assert.Equal(HttpStatusCode.Forbidden, (await clientB.GetAsync($"/api/stores/{sA.Store.Slug}/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await clientB.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/auth/2fa/setup", new BeginTwoFactorSetupCommand(pass))).StatusCode);

            // Verify orderA1 remains Pending in SQL Server
            await using var db = _fixture.CreateDbContext();
            var dbOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderA1.Id);
            Assert.Equal(OrderStatus.Pending, dbOrder.OrderStatus);
            Assert.Equal(custA1Id, dbOrder.CustomerId);
        }
        finally
        {
            await CleanupUsersAsync(custA1Id, custA2Id, custBId);
            await _fixture.DeleteStoresAsync(sA.Store.Id, sB.Store.Id);
        }
    }

    [Fact]
    public async Task BOLA_CrossStoreMerchantNestedResourceIds_AreDeniedWithNoDatabaseSideEffects()
    {
        await using var factory = CreateProductionFactory();
        var sA = await SeedStoreWithCatalogAsync("merch-a");
        var sB = await SeedStoreWithCatalogAsync("merch-b");
        const string pass = "MerchCross!2026";

        var (merchAId, merchAEmail) = await CreateStoreUserAsync(factory, sA.Store.Id, IdentityRoleNames.MerchantOwner, pass);
        var (custBId, custBEmail) = await CreateStoreUserAsync(factory, sB.Store.Id, IdentityRoleNames.Customer, pass);

        try
        {
            using var merchA = CreateCookieClient(factory);
            using var custB = CreateCookieClient(factory);
            Assert.Equal(HttpStatusCode.OK, (await merchA.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/auth/login", new LoginCommand(merchAEmail, pass))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await custB.PostAsJsonAsync($"/api/stores/{sB.Store.Slug}/auth/login", new LoginCommand(custBEmail, pass))).StatusCode);

            // Create an order in Store B
            Assert.Equal(HttpStatusCode.OK, (await custB.PutAsJsonAsync($"/api/stores/{sB.Store.Slug}/cart/items", new UpsertCartItemCommand(sB.Product.Id, 1))).StatusCode);
            using var coMsg = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{sB.Store.Slug}/checkout")
            {
                Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Delivery, sB.Zone.Id, "Store B Addr"))
            };
            coMsg.Headers.Add("Idempotency-Key", $"idem-b-{Guid.NewGuid():N}");
            using var coResp = await custB.SendAsync(coMsg);
            Assert.Equal(HttpStatusCode.Created, coResp.StatusCode);
            var orderB = (await coResp.Content.ReadFromJsonAsync<OrderDto>(JsonOptions))!;

            // Merchant A using Store B route directly => 403
            Assert.Equal(HttpStatusCode.Forbidden, (await merchA.GetAsync($"/api/stores/{sB.Store.Slug}/merchant/orders")).StatusCode);

            // Merchant A using Store A route with Store B nested IDs => 404 (or 400 when referencing cross-store category)
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PutAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/delivery-zones/{sB.Zone.Id}", new UpdateDeliveryZoneCommand(99m, false))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PutAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/categories/{sB.Category.Id}", new UpdateCategoryCommand("Hack", "hack", 99, false))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PutAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/products/{sB.Product.Id}", new UpdateProductCommand(sA.Category.Id, "Hack", null, null, 1m))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PostAsync($"/api/stores/{sA.Store.Slug}/merchant/products/{sB.Product.Id}/archive", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PutAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/products/{sB.Product.Id}/inventory", new { OnHand = 0 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/products/{sB.Product.Id}/images", new AddProductImageCommand("https://evil.example.com/x.png"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.DeleteAsync($"/api/stores/{sA.Store.Slug}/merchant/products/{sB.Product.Id}/images/{sB.Image.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PostAsync($"/api/stores/{sA.Store.Slug}/merchant/orders/{orderB.Id}/confirm", null)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/orders/{orderB.Id}/reject", new { Reason = "X" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/orders/{orderB.Id}/fulfillment", new { NextStatus = FulfillmentStatus.Preparing })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await merchA.PostAsync($"/api/stores/{sA.Store.Slug}/merchant/orders/{orderB.Id}/collect-payment", null)).StatusCode);

            // Creating a product in Store A using Store B's CategoryId in body is rejected
            using var crossCatCreate = await merchA.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/merchant/products", new CreateProductCommand(sB.Category.Id, "CrossCat", null, null, 5m));
            Assert.False(crossCatCreate.IsSuccessStatusCode);

            // Assert Store B resources in SQL Server are completely unchanged
            await using var db = _fixture.CreateDbContext();
            var dbZoneB = await db.DeliveryZones.AsNoTracking().SingleAsync(z => z.Id == sB.Zone.Id);
            var dbProdB = await db.Products.AsNoTracking().SingleAsync(p => p.Id == sB.Product.Id);
            var dbInvB = await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == sB.Product.Id);
            var dbImgBExists = await db.ProductImages.AsNoTracking().AnyAsync(i => i.Id == sB.Image.Id);
            var dbOrderB = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderB.Id);

            Assert.Equal(2.500m, dbZoneB.FlatFee);
            Assert.True(dbZoneB.IsEnabled);
            Assert.Equal(ProductStatus.Published, dbProdB.Status);
            Assert.Equal(20, dbInvB.OnHand);
            Assert.Equal(1, dbInvB.Reserved);
            Assert.True(dbImgBExists);
            Assert.Equal(OrderStatus.Pending, dbOrderB.OrderStatus);
        }
        finally
        {
            await CleanupUsersAsync(merchAId, custBId);
            await _fixture.DeleteStoresAsync(sA.Store.Id, sB.Store.Id);
        }
    }

    [Fact]
    public async Task BOPLA_OverPostingProtectedProperties_IsIgnoredAndDoesNotEscalateOrLeak()
    {
        await using var factory = CreateProductionFactory();
        var sA = await SeedStoreWithCatalogAsync("bopla-a");
        var sB = await SeedStoreWithCatalogAsync("bopla-b");
        Guid registeredId = Guid.Empty;

        try
        {
            using var client = CreateCookieClient(factory);

            // Attempt mass-assignment on Customer registration (forged StoreId, Role, IsActive, TwoFactorEnabled)
            var email = $"bopla-{Guid.NewGuid():N}@retailflow.test";
            using var regResp = await client.PostAsJsonAsync($"/api/stores/{sA.Store.Slug}/auth/register", new
            {
                Email = email,
                DisplayName = "Bopla User",
                Password = "BoplaStrongPass!2026",
                StoreId = sB.Store.Id,
                Role = AppRole.PlatformAdmin,
                IsActive = false,
                TwoFactorEnabled = true,
                MfaEnrollmentStatus = MfaEnrollmentStatus.Enabled
            });
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);
            var dto = (await regResp.Content.ReadFromJsonAsync<AuthenticatedUserDto>(JsonOptions))!;
            registeredId = dto.UserId;

            Assert.Equal(sA.Store.Id, dto.StoreId);
            Assert.Equal(AppRole.Customer, dto.Role);

            await using var db = _fixture.CreateDbContext();
            var dbUser = await db.Users.AsNoTracking().SingleAsync(u => u.Id == registeredId);
            Assert.Equal(sA.Store.Id, dbUser.StoreId);
            Assert.True(dbUser.IsActive);
            Assert.False(dbUser.TwoFactorEnabled);
            Assert.Equal(MfaEnrollmentStatus.NotEnrolled, dbUser.MfaEnrollmentStatus);

            // Public store identity must never expose FulfillmentAddress
            var publicRaw = await client.GetStringAsync($"/api/stores/{sA.Store.Slug}");
            Assert.DoesNotContain("Internal-Dock", publicRaw, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fulfillmentAddress", publicRaw, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (registeredId != Guid.Empty)
            {
                await CleanupUsersAsync(registeredId);
            }
            await _fixture.DeleteStoresAsync(sA.Store.Id, sB.Store.Id);
        }
    }
}
