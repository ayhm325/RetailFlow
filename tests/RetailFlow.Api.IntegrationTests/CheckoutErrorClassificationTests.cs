using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Orders;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class CheckoutErrorClassificationTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public CheckoutErrorClassificationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateFactory(Action<IServiceCollection>? configureServices = null)
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
                        ["Email:RequireConfiguredInProduction"] = "false"
                    };
                    TestDataProtectionCertHelper.ApplyTestCertificate(values);
                    config.AddInMemoryCollection(values);
                });

                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    private async Task<(Guid StoreId, string StoreSlug, Guid ProductId)> SetupStoreAndProductAsync(
        WebApplicationFactory<Program> factory,
        int stock = 10)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"chkerr-{suffix}";

        await using var scope = factory.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
        var catalog = scope.ServiceProvider.GetRequiredService<IMerchantCatalogService>();

        var store = await platform.CreateStoreAsync(
            new CreateStoreCommand("Checkout Err Store", slug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false),
            CancellationToken.None);
        Assert.True(await platform.ApproveStoreAsync(store.Id, CancellationToken.None));

        var cat = await catalog.CreateCategoryAsync(store.Id, new CreateCategoryCommand("Cat", $"cat-{suffix}", 1, true), CancellationToken.None);
        var prod = await catalog.CreateProductAsync(store.Id, new CreateProductCommand(cat.Id, "Prod", null, $"SKU-{suffix}", 20m, ProductStatus.Published, stock), CancellationToken.None);

        return (store.Id, slug, prod.Id);
    }

    private async Task<Guid> RegisterAndLoginCustomerAsync(HttpClient client, string slug)
    {
        var email = $"chk-{Guid.NewGuid():N}@test.local";
        using var resp = await client.PostAsJsonAsync(
            $"/api/stores/{slug}/auth/register",
            new RegisterCustomerCommand(email, "Checkout Tester", "CheckoutPassphrase!2026"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId;
    }

    private async Task CleanupAsync(Guid storeId, params Guid[] userIds)
    {
        await using var db = _fixture.CreateDbContext();
        await db.CheckoutRequests.Where(r => r.StoreId == storeId).ExecuteDeleteAsync();
        await db.CartItems.Where(ci => ci.StoreId == storeId).ExecuteDeleteAsync();
        await db.Carts.Where(c => c.StoreId == storeId).ExecuteDeleteAsync();
        await db.SessionTickets.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
        await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
        await _fixture.DeleteStoresAsync(storeId);
    }

    [Fact]
    public async Task TestA_B_ValidationVsBusinessRule_MappedTo400And422WithStableCodes()
    {
        await using var factory = CreateFactory();
        var (storeId, slug, productId) = await SetupStoreAndProductAsync(factory, stock: 2);
        Guid customerId = Guid.Empty;

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://retail.example.com"),
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
            customerId = await RegisterAndLoginCustomerAsync(client, slug);

            // Test A: Missing Idempotency-Key -> 400 BadRequest (checkout_validation_failed)
            using var missingKeyResp = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/checkout",
                new CheckoutCommand(FulfillmentMode.Pickup, null, null));
            Assert.Equal(HttpStatusCode.BadRequest, missingKeyResp.StatusCode);
            using (var doc = JsonDocument.Parse(await missingKeyResp.Content.ReadAsStringAsync()))
            {
                Assert.Equal(400, doc.RootElement.GetProperty("status").GetInt32());
                Assert.Equal("checkout_validation_failed", doc.RootElement.GetProperty("code").GetString());
            }

            // Test B1: Empty cart -> 422 UnprocessableEntity (checkout_business_rule_failed)
            using var emptyCartReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout")
            {
                Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Pickup, null, null))
            };
            emptyCartReq.Headers.Add("Idempotency-Key", $"empty-{Guid.NewGuid():N}");
            using var emptyCartResp = await client.SendAsync(emptyCartReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, emptyCartResp.StatusCode);
            using (var doc = JsonDocument.Parse(await emptyCartResp.Content.ReadAsStringAsync()))
            {
                Assert.Equal(422, doc.RootElement.GetProperty("status").GetInt32());
                Assert.Equal("checkout_business_rule_failed", doc.RootElement.GetProperty("code").GetString());
            }

            // Test B2: Insufficient stock -> 422 UnprocessableEntity (checkout_business_rule_failed)
            await client.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productId, 5));
            using var stockReq = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout")
            {
                Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Pickup, null, null))
            };
            stockReq.Headers.Add("Idempotency-Key", $"stock-{Guid.NewGuid():N}");
            using var stockResp = await client.SendAsync(stockReq);
            Assert.Equal(HttpStatusCode.UnprocessableEntity, stockResp.StatusCode);
            using (var doc = JsonDocument.Parse(await stockResp.Content.ReadAsStringAsync()))
            {
                Assert.Equal(422, doc.RootElement.GetProperty("status").GetInt32());
                Assert.Equal("checkout_business_rule_failed", doc.RootElement.GetProperty("code").GetString());
            }
        }
        finally
        {
            await CleanupAsync(storeId, customerId);
        }
    }

    private sealed class FaultInjectingCheckoutService : ICheckoutService
    {
        private readonly Exception _exceptionToThrow;

        public FaultInjectingCheckoutService(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public Task<CheckoutResult> CheckoutAsync(
            Guid storeId,
            Guid customerId,
            string idempotencyKey,
            CheckoutCommand command,
            CancellationToken cancellationToken) =>
            Task.FromException<CheckoutResult>(_exceptionToThrow);
    }

    [Fact]
    public async Task TestE_G_H_I_M_UnexpectedDbUpdateAndTimeoutAndException_Return500Or503ProblemDetailsWithoutSqlLeak()
    {
        var (storeId, slug, _) = await SetupStoreAndProductAsync(CreateFactory());

        try
        {
            // 1. Unknown DbUpdateException -> 500 internal_server_error (never 400 ValidationFailed)
            await using (var dbErrFactory = CreateFactory(services =>
            {
                services.RemoveAll<ICheckoutService>();
                services.AddScoped<ICheckoutService>(_ =>
                    new FaultInjectingCheckoutService(new DbUpdateException("SQL Server constraint IX_InternalSecret failed on Server=10.0.0.5")));
            }))
            {
                using var client = dbErrFactory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://retail.example.com"),
                    HandleCookies = true
                });
                client.DefaultRequestHeaders.Add("X-CSRF", "1");
                var uid = await RegisterAndLoginCustomerAsync(client, slug);

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout")
                    {
                        Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Pickup, null, null))
                    };
                    req.Headers.Add("Idempotency-Key", $"db-{Guid.NewGuid():N}");
                    using var resp = await client.SendAsync(req);

                    Assert.Equal(HttpStatusCode.InternalServerError, resp.StatusCode);
                    var body = await resp.Content.ReadAsStringAsync();
                    Assert.DoesNotContain("IX_InternalSecret", body, StringComparison.Ordinal);
                    Assert.DoesNotContain("10.0.0.5", body, StringComparison.Ordinal);

                    using var doc = JsonDocument.Parse(body);
                    Assert.Equal(500, doc.RootElement.GetProperty("status").GetInt32());
                    Assert.Equal("internal_server_error", doc.RootElement.GetProperty("code").GetString());
                    Assert.True(doc.RootElement.TryGetProperty("traceId", out _));
                }
                finally
                {
                    await using var db = _fixture.CreateDbContext();
                    await db.SessionTickets.Where(t => t.UserId == uid).ExecuteDeleteAsync();
                    await db.Users.Where(u => u.Id == uid).ExecuteDeleteAsync();
                }
            }

            // 2. Transient TimeoutException -> 503 service_unavailable
            await using (var timeoutFactory = CreateFactory(services =>
            {
                services.RemoveAll<ICheckoutService>();
                services.AddScoped<ICheckoutService>(_ =>
                    new FaultInjectingCheckoutService(new TimeoutException("SQL connection pool timeout")));
            }))
            {
                using var client = timeoutFactory.CreateClient(new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://retail.example.com"),
                    HandleCookies = true
                });
                client.DefaultRequestHeaders.Add("X-CSRF", "1");
                var uid = await RegisterAndLoginCustomerAsync(client, slug);

                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"/api/stores/{slug}/checkout")
                    {
                        Content = JsonContent.Create(new CheckoutCommand(FulfillmentMode.Pickup, null, null))
                    };
                    req.Headers.Add("Idempotency-Key", $"to-{Guid.NewGuid():N}");
                    using var resp = await client.SendAsync(req);

                    Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
                    var body = await resp.Content.ReadAsStringAsync();
                    Assert.DoesNotContain("pool", body, StringComparison.OrdinalIgnoreCase);

                    using var doc = JsonDocument.Parse(body);
                    Assert.Equal(503, doc.RootElement.GetProperty("status").GetInt32());
                    Assert.Equal("service_unavailable", doc.RootElement.GetProperty("code").GetString());
                }
                finally
                {
                    await using var db = _fixture.CreateDbContext();
                    await db.SessionTickets.Where(t => t.UserId == uid).ExecuteDeleteAsync();
                    await db.Users.Where(u => u.Id == uid).ExecuteDeleteAsync();
                }
            }
        }
        finally
        {
            await CleanupAsync(storeId);
        }
    }

    [Fact]
    public async Task TestJ_Cancellation_PropagatesOperationCanceledException_NotValidationFailed()
    {
        await using var factory = CreateFactory();
        var (storeId, slug, productId) = await SetupStoreAndProductAsync(factory, stock: 5);
        Guid customerId = Guid.Empty;

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://retail.example.com"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");
            customerId = await RegisterAndLoginCustomerAsync(client, slug);
            await client.PutAsJsonAsync($"/api/stores/{slug}/cart/items", new UpsertCartItemCommand(productId, 1));

            await using var scope = factory.Services.CreateAsyncScope();
            var checkoutSvc = scope.ServiceProvider.GetRequiredService<ICheckoutService>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                checkoutSvc.CheckoutAsync(
                    storeId,
                    customerId,
                    $"cancel-{Guid.NewGuid():N}",
                    new CheckoutCommand(FulfillmentMode.Pickup, null, null),
                    cts.Token));
        }
        finally
        {
            await CleanupAsync(storeId, customerId);
        }
    }
}
