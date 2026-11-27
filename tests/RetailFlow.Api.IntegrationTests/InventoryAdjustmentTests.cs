using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Catalog;
using RetailFlow.Infrastructure.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class InventoryAdjustmentTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public InventoryAdjustmentTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class CompetingUpdateInterceptor : SaveChangesInterceptor
    {
        private readonly Func<CancellationToken, Task> _beforeSave;
        private int _triggered;

        public CompetingUpdateInterceptor(Func<CancellationToken, Task> beforeSave)
        {
            _beforeSave = beforeSave;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not null &&
                eventData.Context.ChangeTracker.Entries<InventoryItem>().Any(e => e.State == EntityState.Modified) &&
                Interlocked.CompareExchange(ref _triggered, 1, 0) == 0)
            {
                await _beforeSave(cancellationToken);
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

    private async Task<(Guid AdminId, Guid StoreId, string StoreSlug, string MerchantEmail, Guid ProductId, HttpClient MerchantClient)> SetupMerchantAndProductAsync(
        int onHand = 10,
        int reserved = 4)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var adminEmail = $"admin-inv-{suffix}@retailflow.test";

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
            new CreateStoreCommand("Inv Shop", $"inv-shop-{suffix}", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false, null, "Dock"));
        Assert.Equal(HttpStatusCode.Created, createStoreResp.StatusCode);
        var store = (await createStoreResp.Content.ReadFromJsonAsync<PlatformStoreDto>(JsonOptions))!;

        using var approveResp = await adminClient.PostAsync($"/api/platform/stores/{store.Id}/approve", null);
        Assert.Equal(HttpStatusCode.NoContent, approveResp.StatusCode);

        var merchantEmail = $"merch-inv-{suffix}@example.com";
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
            new CreateProductCommand(category.Id, "Beans", "Single origin", $"SKU-{suffix}", 10.000m, ProductStatus.Published, onHand));
        Assert.Equal(HttpStatusCode.Created, prodResp.StatusCode);
        var product = (await prodResp.Content.ReadFromJsonAsync<MerchantProductDto>(JsonOptions))!;

        if (reserved > 0)
        {
            await using var db = _fixture.CreateDbContext();
            var inv = await db.InventoryItems.SingleAsync(i => i.ProductId == product.Id);
            inv.Reserve(reserved);
            await db.SaveChangesAsync();
        }

        return (adminId, store.Id, store.Slug, merchantEmail, product.Id, merchantClient);
    }

    private static void AssertSanitizedProblemResponse(string body)
    {
        Assert.DoesNotContain("Microsoft.Data.SqlClient", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("EntityFrameworkCore", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Trusted_Connection", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Test1_NegativeOnHand_Returns400ValidationProblem_WithoutDatabaseMutation()
    {
        var (adminId, storeId, slug, _, productId, merchantClient) = await SetupMerchantAndProductAsync(onHand: 10, reserved: 4);

        try
        {
            byte[] initialRowVersion;
            await using (var db = _fixture.CreateDbContext())
            {
                initialRowVersion = (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId)).RowVersion;
            }

            var path = $"/api/stores/{slug}/merchant/products/{productId}/inventory";
            using var resp = await merchantClient.PutAsJsonAsync(path, new { OnHand = -1 });

            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

            var body = await resp.Content.ReadAsStringAsync();
            AssertSanitizedProblemResponse(body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("about:blank", root.GetProperty("type").GetString());
            Assert.Equal("Validation Failed", root.GetProperty("title").GetString());
            Assert.Equal(400, root.GetProperty("status").GetInt32());
            Assert.Equal("validation_error", root.GetProperty("code").GetString());
            Assert.Equal(path, root.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
            Assert.True(root.GetProperty("errors").TryGetProperty("OnHand", out var onHandErrors));
            Assert.True(onHandErrors.GetArrayLength() > 0);

            await using var verifyDb = _fixture.CreateDbContext();
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId);
            Assert.Equal(10, inv.OnHand);
            Assert.Equal(4, inv.Reserved);
            Assert.Equal(initialRowVersion, inv.RowVersion);
        }
        finally
        {
            merchantClient.Dispose();
            await _fixture.DeleteStoresAsync(storeId);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Test2_OnHandBelowReserved_Returns409InventoryReservationConflict_WithoutPartialState()
    {
        var (adminId, storeId, slug, _, productId, merchantClient) = await SetupMerchantAndProductAsync(onHand: 10, reserved: 4);

        try
        {
            byte[] initialRowVersion;
            await using (var db = _fixture.CreateDbContext())
            {
                initialRowVersion = (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId)).RowVersion;
            }

            var path = $"/api/stores/{slug}/merchant/products/{productId}/inventory";
            using var resp = await merchantClient.PutAsJsonAsync(path, new { OnHand = 3 });

            Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
            Assert.Equal("application/problem+json", resp.Content.Headers.ContentType?.MediaType);

            var body = await resp.Content.ReadAsStringAsync();
            AssertSanitizedProblemResponse(body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("about:blank", root.GetProperty("type").GetString());
            Assert.Equal("Conflict", root.GetProperty("title").GetString());
            Assert.Equal(409, root.GetProperty("status").GetInt32());
            Assert.Equal("inventory_reservation_conflict", root.GetProperty("code").GetString());
            Assert.Equal(path, root.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("detail").GetString()));

            await using var verifyDb = _fixture.CreateDbContext();
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId);
            Assert.Equal(10, inv.OnHand);
            Assert.Equal(4, inv.Reserved);
            Assert.Equal(initialRowVersion, inv.RowVersion);
        }
        finally
        {
            merchantClient.Dispose();
            await _fixture.DeleteStoresAsync(storeId);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Test3_ValidInventoryUpdate_Returns204AndUpdatesRowVersionWithoutTouchingReserved()
    {
        var (adminId, storeId, slug, _, productId, merchantClient) = await SetupMerchantAndProductAsync(onHand: 10, reserved: 4);

        try
        {
            byte[] initialRowVersion;
            await using (var db = _fixture.CreateDbContext())
            {
                initialRowVersion = (await db.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId)).RowVersion;
            }

            using var resp = await merchantClient.PutAsJsonAsync(
                $"/api/stores/{slug}/merchant/products/{productId}/inventory",
                new { OnHand = 12 });

            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

            await using var verifyDb = _fixture.CreateDbContext();
            var inv = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId);
            Assert.Equal(12, inv.OnHand);
            Assert.Equal(4, inv.Reserved);
            Assert.NotEqual(initialRowVersion, inv.RowVersion);
        }
        finally
        {
            merchantClient.Dispose();
            await _fixture.DeleteStoresAsync(storeId);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Test4_RealSqlRowVersionConflict_ThrowsInventoryConcurrencyConflict_AndApiReturns409WithoutLostUpdate()
    {
        var (adminId, storeId, slug, merchantEmail, productId, merchantClient) = await SetupMerchantAndProductAsync(onHand: 10, reserved: 4);

        try
        {
            // Part A: Service-level deterministic SQL RowVersion conflict
            var serviceInterceptor = new CompetingUpdateInterceptor(async ct =>
            {
                await using var competingDb = _fixture.CreateDbContext();
                await competingDb.InventoryItems
                    .Where(i => i.ProductId == productId)
                    .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, 15), ct);
            });

            await using (var interceptedDb = CreateInterceptedDbContext(serviceInterceptor))
            {
                var service = new MerchantCatalogService(interceptedDb);
                var ex = await Assert.ThrowsAsync<InventoryConcurrencyConflictException>(() =>
                    service.SetInventoryOnHandAsync(storeId, productId, 20, CancellationToken.None));
                Assert.Equal(storeId, ex.StoreId);
                Assert.Equal(productId, ex.ProductId);
                Assert.Equal(nameof(MerchantCatalogService.SetInventoryOnHandAsync), ex.Operation);
                Assert.IsType<DbUpdateConcurrencyException>(ex.InnerException);
            }

            await using (var verifyDb = _fixture.CreateDbContext())
            {
                var invAfterPartA = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId);
                Assert.Equal(15, invAfterPartA.OnHand);
                Assert.Equal(4, invAfterPartA.Reserved);
            }

            // Part B: Full API HTTP path under SQL RowVersion conflict -> 409 inventory_concurrency_conflict
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
                        var apiInterceptor = new CompetingUpdateInterceptor(async ct =>
                        {
                            await using var competingDb = _fixture.CreateDbContext();
                            await competingDb.InventoryItems
                                .Where(i => i.ProductId == productId)
                                .ExecuteUpdateAsync(s => s.SetProperty(i => i.OnHand, 18), ct);
                        });

                        services.RemoveAll<DbContextOptions<RetailFlowDbContext>>();
                        services.RemoveAll<RetailFlowDbContext>();
                        services.AddSingleton(apiInterceptor);
                        services.AddDbContext<RetailFlowDbContext>((sp, options) =>
                        {
                            options.UseSqlServer(_fixture.ConnectionString)
                                .AddInterceptors(sp.GetRequiredService<CompetingUpdateInterceptor>());
                        });
                    });
                });

            using var raceClient = raceFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
            raceClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var loginResp = await raceClient.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(merchantEmail, "MerchPassphrase!1"));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

            var path = $"/api/stores/{slug}/merchant/products/{productId}/inventory";
            using var conflictResp = await raceClient.PutAsJsonAsync(path, new { OnHand = 25 });

            Assert.Equal(HttpStatusCode.Conflict, conflictResp.StatusCode);
            Assert.Equal("application/problem+json", conflictResp.Content.Headers.ContentType?.MediaType);

            var body = await conflictResp.Content.ReadAsStringAsync();
            AssertSanitizedProblemResponse(body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            Assert.Equal("about:blank", root.GetProperty("type").GetString());
            Assert.Equal("Conflict", root.GetProperty("title").GetString());
            Assert.Equal(409, root.GetProperty("status").GetInt32());
            Assert.Equal("inventory_concurrency_conflict", root.GetProperty("code").GetString());
            Assert.Equal(path, root.GetProperty("instance").GetString());
            Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("traceId").GetString()));

            await using var finalVerifyDb = _fixture.CreateDbContext();
            var finalInv = await finalVerifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == productId);
            Assert.Equal(18, finalInv.OnHand);
            Assert.Equal(4, finalInv.Reserved);
        }
        finally
        {
            merchantClient.Dispose();
            await _fixture.DeleteStoresAsync(storeId);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task Test5_MissingOrCrossStoreProduct_Returns404WithoutMutatingOtherStoreInventory()
    {
        var (adminId, storeAId, slugA, _, _, merchantClientA) = await SetupMerchantAndProductAsync(onHand: 10, reserved: 0);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var storeB = Store.Create(Guid.NewGuid(), "Other Store", $"other-inv-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settingsB = StoreSettings.Create(Guid.NewGuid(), storeB, Jod, "Asia/Amman", true, false, null, "Dock B");
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "Cat B", "cat-b", true, 1);
        var prodB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Prod B", 15.000m, settingsB, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);
        var invB = InventoryItem.Create(Guid.NewGuid(), storeB.Id, prodB.Id, onHand: 7, reserved: 1);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(storeB);
            db.StoreSettings.Add(settingsB);
            db.Categories.Add(catB);
            db.Products.Add(prodB);
            db.InventoryItems.Add(invB);
            await db.SaveChangesAsync();
        }

        try
        {
            using var missingResp = await merchantClientA.PutAsJsonAsync(
                $"/api/stores/{slugA}/merchant/products/{Guid.NewGuid()}/inventory",
                new { OnHand = 50 });
            Assert.Equal(HttpStatusCode.NotFound, missingResp.StatusCode);

            using var crossStoreResp = await merchantClientA.PutAsJsonAsync(
                $"/api/stores/{slugA}/merchant/products/{prodB.Id}/inventory",
                new { OnHand = 50 });
            Assert.Equal(HttpStatusCode.NotFound, crossStoreResp.StatusCode);

            await using var verifyDb = _fixture.CreateDbContext();
            var reloadedB = await verifyDb.InventoryItems.AsNoTracking().SingleAsync(i => i.ProductId == prodB.Id);
            Assert.Equal(7, reloadedB.OnHand);
            Assert.Equal(1, reloadedB.Reserved);
        }
        finally
        {
            merchantClientA.Dispose();
            await _fixture.DeleteStoresAsync(storeAId, storeB.Id);
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }
}
