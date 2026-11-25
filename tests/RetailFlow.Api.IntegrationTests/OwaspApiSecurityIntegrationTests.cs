using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using RetailFlow.Application.Catalog;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Api.IntegrationTests;

public sealed class OwaspApiSecurityIntegrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);

    private readonly CatalogSqlTestFixture _fixture;

    public OwaspApiSecurityIntegrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task API1_BOLA_CrossStoreProductIdAndCategoryFilter_AreBlocked()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var storeA = Store.Create(Guid.NewGuid(), "Store A", $"bola-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settingsA = StoreSettings.Create(Guid.NewGuid(), storeA, Jod, "Asia/Amman", true, false, null, "Secret Dock A");
        var catA = Category.Create(Guid.NewGuid(), storeA.Id, "Cat A", "cat-a", true, 1);
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Prod A", 10.000m, settingsA, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);

        var storeB = Store.Create(Guid.NewGuid(), "Store B", $"bola-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        var settingsB = StoreSettings.Create(Guid.NewGuid(), storeB, Jod, "Asia/Amman", true, false, null, "Secret Dock B");

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            db.Categories.Add(catA);
            db.Products.Add(prodA);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            // Store B slug + Store A productId must return 404 ProblemDetails (API1:2023 BOLA)
            using var productResp = await client.GetAsync($"/api/stores/{storeB.Slug}/catalog/products/{prodA.Id}");
            Assert.Equal(HttpStatusCode.NotFound, productResp.StatusCode);

            // Store B slug + Store A categoryId filter must return empty list
            var filterResp = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{storeB.Slug}/catalog/products?categoryId={catA.Id}",
                JsonOptions);
            Assert.NotNull(filterResp);
            Assert.Empty(filterResp!.Items);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task A05_SecurityHeaders_ArePresentOnApiResponses()
    {
        using var client = _fixture.CreateHttpsClient();
        using var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var nosniff));
        Assert.Contains("nosniff", nosniff);
        Assert.True(response.Headers.TryGetValues("X-Frame-Options", out var xfo));
        Assert.Contains("DENY", xfo);
        Assert.True(response.Headers.TryGetValues("Referrer-Policy", out var refPolicy));
        Assert.Contains("strict-origin-when-cross-origin", refPolicy);
    }

    [Fact]
    public async Task API4_RateLimiting_ExceedingLimit_Returns429ProblemDetails_AndHealthIsExempt()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "localhost",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["RateLimiting:PermitLimit"] = "2",
                        ["RateLimiting:WindowSeconds"] = "60"
                    });
                });
            });

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

        // First two API requests succeed (or 404 for unknown slug, still permitted through limiter)
        using var r1 = await client.GetAsync("/api/stores/nonexistent-rate-1");
        using var r2 = await client.GetAsync("/api/stores/nonexistent-rate-2");
        Assert.Equal(HttpStatusCode.NotFound, r1.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, r2.StatusCode);

        // Third API request is throttled with 429 ProblemDetails
        using var r3 = await client.GetAsync("/api/stores/nonexistent-rate-3");
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        Assert.Equal("application/problem+json", r3.Content.Headers.ContentType?.MediaType);

        // Health probes remain exempt from rate limiting
        using var health = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
