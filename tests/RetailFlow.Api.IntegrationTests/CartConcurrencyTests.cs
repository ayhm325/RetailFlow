using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
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
using RetailFlow.Domain.Stores;

namespace RetailFlow.Api.IntegrationTests;

public sealed class CartConcurrencyTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public CartConcurrencyTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateInstanceFactory()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "localhost",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["PlatformSecurity:RequireAdminMfa"] = "false"
                    });
                });
            });
    }

    private async Task<(Guid StoreId, string StoreSlug, Guid ProductAId, Guid ProductBId)> SetupStoreAndProductsAsync(
        WebApplicationFactory<Program> factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"cartcc-{suffix}";

        await using var scope = factory.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
        var catalog = scope.ServiceProvider.GetRequiredService<IMerchantCatalogService>();

        var store = await platform.CreateStoreAsync(
            new CreateStoreCommand("Cart Concurrency Store", slug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false),
            CancellationToken.None);
        Assert.True(await platform.ApproveStoreAsync(store.Id, CancellationToken.None));

        var cat = await catalog.CreateCategoryAsync(store.Id, new CreateCategoryCommand("General", $"gen-{suffix}", 1, true), CancellationToken.None);
        var pA = await catalog.CreateProductAsync(store.Id, new CreateProductCommand(cat.Id, "Item A", null, $"SKU-A-{suffix}", 10m, ProductStatus.Published, 50), CancellationToken.None);
        var pB = await catalog.CreateProductAsync(store.Id, new CreateProductCommand(cat.Id, "Item B", null, $"SKU-B-{suffix}", 15m, ProductStatus.Published, 50), CancellationToken.None);

        return (store.Id, slug, pA.Id, pB.Id);
    }

    private async Task<Guid> RegisterCustomerAsync(HttpClient client, string slug, string email)
    {
        using var resp = await client.PostAsJsonAsync(
            $"/api/stores/{slug}/auth/register",
            new RegisterCustomerCommand(email, "Cart Tester", "CartPassphrase!2026"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId;
    }

    private async Task CleanupAsync(Guid storeId, params Guid[] userIds)
    {
        await using var db = _fixture.CreateDbContext();
        await db.CartItems.Where(ci => ci.StoreId == storeId).ExecuteDeleteAsync();
        await db.Carts.Where(c => c.StoreId == storeId).ExecuteDeleteAsync();
        await db.SessionTickets.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
        await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        await _fixture.DeleteStoresAsync(storeId);
    }

    [Fact]
    public async Task TestA_B_H_L_ConcurrentCartCreationAndSameItemAdd_ProducesSingleCartAndSingleLine()
    {
        await using var factory1 = CreateInstanceFactory();
        await using var factory2 = CreateInstanceFactory();
        var (storeId, slug, productAId, _) = await SetupStoreAndProductsAsync(factory1);
        Guid customerId = Guid.Empty;

        try
        {
            using var regClient = factory1.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            regClient.DefaultRequestHeaders.Add("X-CSRF", "1");
            customerId = await RegisterCustomerAsync(regClient, slug, $"c1-{slug}@test.local");

            // 5 concurrent UpsertItemAsync calls across 2 independent instances on an empty cart for the same product
            var tasks = Enumerable.Range(1, 5).Select(async i =>
            {
                var targetFactory = i % 2 == 0 ? factory2 : factory1;
                await using var scope = targetFactory.Services.CreateAsyncScope();
                var cartSvc = scope.ServiceProvider.GetRequiredService<ICartService>();
                return await cartSvc.UpsertItemAsync(storeId, customerId, new UpsertCartItemCommand(productAId, i), CancellationToken.None);
            });

            var results = await Task.WhenAll(tasks);
            Assert.All(results, r => Assert.NotEmpty(r.Version));

            await using var db = _fixture.CreateDbContext();
            var cartCount = await db.Carts.CountAsync(c => c.StoreId == storeId && c.CustomerId == customerId);
            var itemCount = await db.CartItems.CountAsync(ci => ci.StoreId == storeId && ci.ProductId == productAId);
            Assert.Equal(1, cartCount);
            Assert.Equal(1, itemCount);
        }
        finally
        {
            await CleanupAsync(storeId, customerId);
        }
    }

    [Fact]
    public async Task TestC_I_J_ConcurrentAddOfDifferentItemsAcrossTwoInstances_PreservesBothItemsAndAdvancesVersion()
    {
        await using var instance1 = CreateInstanceFactory();
        await using var instance2 = CreateInstanceFactory();
        var (storeId, slug, productAId, productBId) = await SetupStoreAndProductsAsync(instance1);
        Guid customerId = Guid.Empty;

        try
        {
            using var client1 = instance1.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client1.DefaultRequestHeaders.Add("X-CSRF", "1");
            var email = $"multi-{slug}@test.local";
            customerId = await RegisterCustomerAsync(client1, slug, email);

            // Seed empty cart row first so both instances race on the same Cart.RowVersion
            await client1.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productAId, 1));
            var initialCart = await client1.GetFromJsonAsync<CartDto>($"/api/stores/{slug}/cart");
            Assert.NotNull(initialCart);
            var v1 = initialCart!.Version;

            await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var s1 = instance1.Services.CreateAsyncScope();
                    await s1.ServiceProvider.GetRequiredService<ICartService>()
                        .UpsertItemAsync(storeId, customerId, new UpsertCartItemCommand(productAId, 4), CancellationToken.None);
                }),
                Task.Run(async () =>
                {
                    await using var s2 = instance2.Services.CreateAsyncScope();
                    await s2.ServiceProvider.GetRequiredService<ICartService>()
                        .UpsertItemAsync(storeId, customerId, new UpsertCartItemCommand(productBId, 2), CancellationToken.None);
                }));

            var finalCart = await client1.GetFromJsonAsync<CartDto>($"/api/stores/{slug}/cart");
            Assert.NotNull(finalCart);
            Assert.NotEqual(v1, finalCart!.Version);
            Assert.Equal(2, finalCart.Items.Count);
            Assert.Equal(4, finalCart.Items.Single(i => i.ProductId == productAId).Quantity);
            Assert.Equal(2, finalCart.Items.Single(i => i.ProductId == productBId).Quantity);
        }
        finally
        {
            await CleanupAsync(storeId, customerId);
        }
    }

    [Fact]
    public async Task TestD_StaleIfMatchETag_Returns412PreconditionFailed_AndPreservesNewerState()
    {
        await using var factory = CreateInstanceFactory();
        var (storeId, slug, productAId, _) = await SetupStoreAndProductsAsync(factory);
        Guid customerId = Guid.Empty;

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
            customerId = await RegisterCustomerAsync(client, slug, $"etag-{slug}@test.local");

            // Tab A adds item -> gets V1
            using var put1 = await client.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productAId, 1));
            Assert.Equal(HttpStatusCode.OK, put1.StatusCode);
            var etagV1 = put1.Headers.ETag?.Tag;
            Assert.False(string.IsNullOrWhiteSpace(etagV1));

            // Tab A updates to Qty = 5 -> gets V2
            using var reqV2 = new HttpRequestMessage(HttpMethod.Put, $"/api/stores/{slug}/cart/items")
            {
                Content = JsonContent.Create(new UpsertCartItemCommand(productAId, 5))
            };
            reqV2.Headers.TryAddWithoutValidation("If-Match", etagV1);
            using var put2 = await client.SendAsync(reqV2);
            Assert.Equal(HttpStatusCode.OK, put2.StatusCode);
            var etagV2 = put2.Headers.ETag?.Tag;
            Assert.NotEqual(etagV1, etagV2);

            // Tab B tries stale update with V1 -> 412 Precondition Failed with code = cart_concurrency_conflict
            using var stalePut = new HttpRequestMessage(HttpMethod.Put, $"/api/stores/{slug}/cart/items")
            {
                Content = JsonContent.Create(new UpsertCartItemCommand(productAId, 2))
            };
            stalePut.Headers.TryAddWithoutValidation("If-Match", etagV1);
            using var staleResp = await client.SendAsync(stalePut);
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleResp.StatusCode);

            var problemJson = await staleResp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(problemJson);
            Assert.Equal("cart_concurrency_conflict", doc.RootElement.GetProperty("code").GetString());

            // Stale DELETE with V1 -> also 412 Precondition Failed
            using var staleDel = new HttpRequestMessage(HttpMethod.Delete, $"/api/stores/{slug}/cart/items/{productAId}");
            staleDel.Headers.TryAddWithoutValidation("If-Match", etagV1);
            using var staleDelResp = await client.SendAsync(staleDel);
            Assert.Equal(HttpStatusCode.PreconditionFailed, staleDelResp.StatusCode);

            // Verify newer Qty = 5 was NOT overwritten
            var currentCart = await client.GetFromJsonAsync<CartDto>($"/api/stores/{slug}/cart");
            Assert.Equal(5, Assert.Single(currentCart!.Items).Quantity);
        }
        finally
        {
            await CleanupAsync(storeId, customerId);
        }
    }

    [Fact]
    public async Task TestE_F_G_ConcurrentUpdateRemoveClear_RemainConsistentWithoutOrphans()
    {
        await using var factory = CreateInstanceFactory();
        var (storeId, slug, productAId, productBId) = await SetupStoreAndProductsAsync(factory);
        Guid customerId = Guid.Empty;

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
            customerId = await RegisterCustomerAsync(client, slug, $"rem-{slug}@test.local");

            await client.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productAId, 2));

            // Concurrent Remove + Remove -> both succeed cleanly without 500
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var s1 = factory.Services.CreateAsyncScope();
                    await s1.ServiceProvider.GetRequiredService<ICartService>().RemoveItemAsync(storeId, customerId, productAId, CancellationToken.None);
                }),
                Task.Run(async () =>
                {
                    await using var s2 = factory.Services.CreateAsyncScope();
                    await s2.ServiceProvider.GetRequiredService<ICartService>().RemoveItemAsync(storeId, customerId, productAId, CancellationToken.None);
                }));

            var afterRemove = await client.GetFromJsonAsync<CartDto>($"/api/stores/{slug}/cart");
            Assert.Empty(afterRemove!.Items);

            // Concurrent Clear + Add different item -> deterministic valid state, no corruption
            await client.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productAId, 3));
            await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var s1 = factory.Services.CreateAsyncScope();
                    await s1.ServiceProvider.GetRequiredService<ICartService>().ClearCartAsync(storeId, customerId, CancellationToken.None);
                }),
                Task.Run(async () =>
                {
                    await using var s2 = factory.Services.CreateAsyncScope();
                    await s2.ServiceProvider.GetRequiredService<ICartService>().UpsertItemAsync(storeId, customerId, new UpsertCartItemCommand(productBId, 1), CancellationToken.None);
                }));

            var finalCart = await client.GetFromJsonAsync<CartDto>($"/api/stores/{slug}/cart");
            Assert.NotNull(finalCart);
            Assert.All(finalCart!.Items, item => Assert.True(item.Quantity > 0));
        }
        finally
        {
            await CleanupAsync(storeId, customerId);
        }
    }

    [Fact]
    public async Task TestM_N_OwnershipTenantIsolation_And_InvalidQuantityRejected()
    {
        await using var factory = CreateInstanceFactory();
        var (storeAId, slugA, productAId, _) = await SetupStoreAndProductsAsync(factory);
        var (storeBId, slugB, _, _) = await SetupStoreAndProductsAsync(factory);
        Guid custA = Guid.Empty;

        try
        {
            using var clientA = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            clientA.DefaultRequestHeaders.Add("X-CSRF", "1");
            custA = await RegisterCustomerAsync(clientA, slugA, $"iso-{slugA}@test.local");

            // Store A customer cannot mutate Store B cart -> 403 Forbidden
            using var crossTenant = await clientA.PutAsJsonAsync($"/api/stores/{slugB}/cart/items", new UpsertCartItemCommand(productAId, 1));
            Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);

            // Zero and negative quantity rejected
            await using var scope = factory.Services.CreateAsyncScope();
            var cartSvc = scope.ServiceProvider.GetRequiredService<ICartService>();
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                cartSvc.UpsertItemAsync(storeAId, custA, new UpsertCartItemCommand(productAId, 0), CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                cartSvc.UpsertItemAsync(storeAId, custA, new UpsertCartItemCommand(productAId, -5), CancellationToken.None));
        }
        finally
        {
            await CleanupAsync(storeAId, custA);
            await CleanupAsync(storeBId);
        }
    }
}
