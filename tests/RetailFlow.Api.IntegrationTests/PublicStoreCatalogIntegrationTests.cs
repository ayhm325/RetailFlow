using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using RetailFlow.Api.Hosting;
using RetailFlow.Application.Catalog;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PublicStoreCatalogIntegrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly Currency Usd = Currency.Create("USD", "840", 2);
    private static readonly Currency Eur = Currency.Create("EUR", "978", 2);
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private static readonly Currency Kwd = Currency.Create("KWD", "414", 3);
    private static readonly Currency Sar = Currency.Create("SAR", "682", 2);
    private static readonly Currency Jpy = Currency.Create("JPY", "392", 0);

    private readonly CatalogSqlTestFixture _fixture;

    public PublicStoreCatalogIntegrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ActiveStore_ReturnsCategoriesAndProducts()
    {
        var suffix = NewSuffix();
        var (store, settings) = CreateStoreWithSettings($"Active Store {suffix}", $"active-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);
        var category = Category.Create(Guid.NewGuid(), store.Id, "Electronics", $"electronics-{suffix}", isActive: true, sortOrder: 1);
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Wireless Mouse", 29.99m, settings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow, description: "Ergonomic wireless mouse", sku: $"SKU-{suffix}-1");
        var image = ProductImage.Create(Guid.NewGuid(), store.Id, product.Id, "https://cdn.example.com/mouse.png", sortOrder: 0, altText: "Wireless Mouse");
        var inventory = InventoryItem.Create(Guid.NewGuid(), store.Id, product.Id, onHand: 12, reserved: 2);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Categories.Add(category);
            db.Products.Add(product);
            db.ProductImages.Add(image);
            db.InventoryItems.Add(inventory);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            using var catResponse = await client.GetAsync($"/api/stores/{store.Slug}/catalog/categories");
            Assert.Equal(HttpStatusCode.OK, catResponse.StatusCode);
            var catPayload = await catResponse.Content.ReadFromJsonAsync<CatalogCategoriesResponse>(JsonOptions);
            Assert.NotNull(catPayload);
            Assert.Equal(store.Name, catPayload.StoreName);
            Assert.Equal(store.Slug, catPayload.StoreSlug);
            Assert.Single(catPayload.Categories);
            Assert.Equal(category.Id, catPayload.Categories[0].Id);

            using var listResponse = await client.GetAsync($"/api/stores/{store.Slug}/catalog/products");
            Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
            var listPayload = await listResponse.Content.ReadFromJsonAsync<CatalogProductsResponse>(JsonOptions);
            Assert.NotNull(listPayload);
            Assert.Equal(1, listPayload.TotalCount);
            var summary = Assert.Single(listPayload.Items);
            Assert.Equal(product.Id, summary.Id);
            Assert.Equal("USD", summary.CurrencyCode);
            Assert.Equal("https://cdn.example.com/mouse.png", summary.PrimaryImageUrl);
            Assert.True(summary.IsAvailable);

            using var detailResponse = await client.GetAsync($"/api/stores/{store.Slug}/catalog/products/{product.Id}");
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detailPayload = await detailResponse.Content.ReadFromJsonAsync<CatalogProductDetailsDto>(JsonOptions);
            Assert.NotNull(detailPayload);
            Assert.Equal(product.Id, detailPayload.Id);
            Assert.Equal("USD", detailPayload.CurrencyCode);
            Assert.True(detailPayload.IsAvailable);
            Assert.Single(detailPayload.Images);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task UnknownSlug_Returns404ProblemDetails()
    {
        using var client = _fixture.CreateHttpsClient();
        var unknownSlug = $"missing-{NewSuffix()}";

        foreach (var path in new[]
        {
            $"/api/stores/{unknownSlug}/catalog/categories",
            $"/api/stores/{unknownSlug}/catalog/products",
            $"/api/stores/{unknownSlug}/catalog/products/{Guid.NewGuid()}"
        })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
            Assert.NotNull(problem);
            Assert.Equal(404, problem.Status);
        }
    }

    [Theory]
    [InlineData(StoreStatus.PendingApproval)]
    [InlineData(StoreStatus.Suspended)]
    [InlineData(StoreStatus.Rejected)]
    public async Task NonActiveStores_Return404ProblemDetails(StoreStatus status)
    {
        var suffix = NewSuffix();
        var (store, settings) = CreateStoreWithSettings($"Inactive Store {suffix}", $"inactive-{suffix}", StoreType.Physical, status, Sar);
        var category = Category.Create(Guid.NewGuid(), store.Id, "Cat", $"cat-{suffix}", true, 1);
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Prod", 10m, settings, Sar, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            foreach (var path in new[]
            {
                $"/api/stores/{store.Slug}/catalog/categories",
                $"/api/stores/{store.Slug}/catalog/products",
                $"/api/stores/{store.Slug}/catalog/products/{product.Id}"
            })
            {
                using var response = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task Categories_ReturnOnlyActiveOrderedCategoriesForTargetStore()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Store A", $"store-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);
        var (storeB, settingsB) = CreateStoreWithSettings("Store B", $"store-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Eur);

        var catA2 = Category.Create(Guid.NewGuid(), storeA.Id, "Zeta", $"zeta-{suffix}", true, 2);
        var catA1b = Category.Create(Guid.NewGuid(), storeA.Id, "Beta", $"beta-{suffix}", true, 1);
        var catA1a = Category.Create(Guid.NewGuid(), storeA.Id, "Alpha", $"alpha-{suffix}", true, 1);
        var catAInactive = Category.Create(Guid.NewGuid(), storeA.Id, "Hidden", $"hidden-{suffix}", false, 0);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "OtherStore", $"other-{suffix}", true, 0);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            db.Categories.AddRange(catA2, catA1b, catA1a, catAInactive, catB);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();
            using var response = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/categories");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<CatalogCategoriesResponse>(JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(
                [catA1a.Id, catA1b.Id, catA2.Id],
                payload.Categories.Select(c => c.Id).ToArray());
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task Products_ExcludeDraftArchivedInactiveCategoryAndOtherStoreProducts_AndReactivateCleanly()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Store A", $"vis-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);
        var (storeB, settingsB) = CreateStoreWithSettings("Store B", $"vis-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Eur);

        var catActiveA = Category.Create(Guid.NewGuid(), storeA.Id, "General", $"gen-a-{suffix}", true, 1);
        var catInactiveA = Category.Create(Guid.NewGuid(), storeA.Id, "Hidden", $"hid-a-{suffix}", false, 2);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "General", $"gen-b-{suffix}", true, 1);

        var publishedA = Product.Create(Guid.NewGuid(), storeA.Id, catActiveA.Id, "Published A", 15m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var publishedInInactiveCatA = Product.Create(Guid.NewGuid(), storeA.Id, catInactiveA.Id, "Hidden Cat Product", 15m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var draftA = Product.Create(Guid.NewGuid(), storeA.Id, catActiveA.Id, "Draft A", 15m, settingsA, Usd, ProductStatus.Draft, DateTimeOffset.UtcNow);
        var archivedA = Product.Create(Guid.NewGuid(), storeA.Id, catActiveA.Id, "Archived A", 15m, settingsA, Usd, ProductStatus.Archived, DateTimeOffset.UtcNow);
        var publishedB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Published B", 15m, settingsB, Eur, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            db.Categories.AddRange(catActiveA, catInactiveA, catB);
            db.Products.AddRange(publishedA, publishedInInactiveCatA, draftA, archivedA, publishedB);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            var listPayload = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{storeA.Slug}/catalog/products",
                JsonOptions);
            Assert.NotNull(listPayload);
            var single = Assert.Single(listPayload.Items);
            Assert.Equal(publishedA.Id, single.Id);

            foreach (var hiddenId in new[] { publishedInInactiveCatA.Id, draftA.Id, archivedA.Id, publishedB.Id })
            {
                using var detailResp = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/products/{hiddenId}");
                Assert.Equal(HttpStatusCode.NotFound, detailResp.StatusCode);
            }

            var byInactiveId = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{storeA.Slug}/catalog/products?categoryId={catInactiveA.Id}",
                JsonOptions);
            Assert.NotNull(byInactiveId);
            Assert.Empty(byInactiveId.Items);

            var byInactiveSlug = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{storeA.Slug}/catalog/products?categorySlug={catInactiveA.Slug}",
                JsonOptions);
            Assert.NotNull(byInactiveSlug);
            Assert.Empty(byInactiveSlug.Items);

            await using (var db = _fixture.CreateDbContext())
            {
                await db.Categories
                    .Where(c => c.Id == catInactiveA.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, true));
            }

            var reactivatedList = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{storeA.Slug}/catalog/products",
                JsonOptions);
            Assert.NotNull(reactivatedList);
            Assert.Equal(2, reactivatedList.TotalCount);
            Assert.Contains(reactivatedList.Items, i => i.Id == publishedInInactiveCatA.Id);

            using var reactivatedDetails = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/products/{publishedInInactiveCatA.Id}");
            Assert.Equal(HttpStatusCode.OK, reactivatedDetails.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task Products_SearchFilterSortAndPagination_BehaveDeterministically()
    {
        var suffix = NewSuffix();
        var (store, settings) = CreateStoreWithSettings("Catalog Store", $"cat-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Eur);
        var catBooks = Category.Create(Guid.NewGuid(), store.Id, "Books", $"books-{suffix}", true, 1);
        var catGear = Category.Create(Guid.NewGuid(), store.Id, "Gear", $"gear-{suffix}", true, 2);

        var t0 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var p1 = Product.Create(Guid.NewGuid(), store.Id, catBooks.Id, "Alpha Book", 30m, settings, Eur, ProductStatus.Published, t0, sku: $"BK-{suffix}-01");
        var p2 = Product.Create(Guid.NewGuid(), store.Id, catBooks.Id, "Gamma Guide", 10m, settings, Eur, ProductStatus.Published, t0.AddMinutes(1), sku: $"BK-{suffix}-02");
        var p3 = Product.Create(Guid.NewGuid(), store.Id, catGear.Id, "Beta Lamp", 20m, settings, Eur, ProductStatus.Published, t0.AddMinutes(2), sku: $"GR-{suffix}-03");
        var p4 = Product.Create(Guid.NewGuid(), store.Id, catGear.Id, "Delta Mug", 5m, settings, Eur, ProductStatus.Published, t0.AddMinutes(3), sku: $"GR-{suffix}-04");

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Categories.AddRange(catBooks, catGear);
            db.Products.AddRange(p1, p2, p3, p4);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            var skuResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?search=gr-{suffix}-03",
                JsonOptions);
            Assert.NotNull(skuResult);
            Assert.Equal([p3.Id], skuResult.Items.Select(i => i.Id).ToArray());

            var catIdResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catBooks.Id}&sort=price_asc",
                JsonOptions);
            Assert.NotNull(catIdResult);
            Assert.Equal([p2.Id, p1.Id], catIdResult.Items.Select(i => i.Id).ToArray());

            var matchingCatResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catBooks.Id}&categorySlug={catBooks.Slug}",
                JsonOptions);
            Assert.NotNull(matchingCatResult);
            Assert.Equal(2, matchingCatResult.TotalCount);

            var conflictingCatResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catBooks.Id}&categorySlug={catGear.Slug}",
                JsonOptions);
            Assert.NotNull(conflictingCatResult);
            Assert.Empty(conflictingCatResult.Items);

            var page1 = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?sort=price_desc&page=1&pageSize=2",
                JsonOptions);
            Assert.NotNull(page1);
            Assert.Equal(4, page1.TotalCount);
            Assert.Equal(2, page1.TotalPages);
            Assert.Equal([p1.Id, p3.Id], page1.Items.Select(i => i.Id).ToArray());

            var page2 = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?sort=price_desc&page=2&pageSize=2",
                JsonOptions);
            Assert.NotNull(page2);
            Assert.Equal([p2.Id, p4.Id], page2.Items.Select(i => i.Id).ToArray());

            var page3 = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?sort=name_asc&page=3&pageSize=2",
                JsonOptions);
            Assert.NotNull(page3);
            Assert.Empty(page3.Items);
            Assert.Equal(4, page3.TotalCount);

            var maxPage = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?page={int.MaxValue}&pageSize=50",
                JsonOptions);
            Assert.NotNull(maxPage);
            Assert.Equal(int.MaxValue, maxPage.Page);
            Assert.Empty(maxPage.Items);
            Assert.Equal(4, maxPage.TotalCount);

            var nameDesc = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categorySlug={catBooks.Slug}&sort=name_desc",
                JsonOptions);
            Assert.NotNull(nameDesc);
            Assert.Equal([p2.Id, p1.Id], nameDesc.Items.Select(i => i.Id).ToArray());
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task Inventory_ProjectsAvailabilityFromOnHandMinusReservedAndMissingRow()
    {
        var suffix = NewSuffix();
        var (store, settings) = CreateStoreWithSettings("Inv Store", $"inv-{suffix}", StoreType.Physical, StoreStatus.Active, Usd);
        var category = Category.Create(Guid.NewGuid(), store.Id, "Cat", $"inv-cat-{suffix}", true, 1);

        var availableProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Available", 10m, settings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var reservedOutProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "ReservedOut", 10m, settings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var noInventoryProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "NoInventory", 10m, settings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);

        var availableInventory = InventoryItem.Create(Guid.NewGuid(), store.Id, availableProd.Id, onHand: 10, reserved: 3);
        var reservedOutInventory = InventoryItem.Create(Guid.NewGuid(), store.Id, reservedOutProd.Id, onHand: 5, reserved: 5);

        Assert.Equal(7, availableInventory.Available);
        Assert.Equal(0, reservedOutInventory.Available);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            db.Categories.Add(category);
            db.Products.AddRange(availableProd, reservedOutProd, noInventoryProd);
            db.InventoryItems.AddRange(availableInventory, reservedOutInventory);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            var availableDetails = await client.GetFromJsonAsync<CatalogProductDetailsDto>(
                $"/api/stores/{store.Slug}/catalog/products/{availableProd.Id}",
                JsonOptions);
            Assert.NotNull(availableDetails);
            Assert.True(availableDetails.IsAvailable);

            var reservedOutDetails = await client.GetFromJsonAsync<CatalogProductDetailsDto>(
                $"/api/stores/{store.Slug}/catalog/products/{reservedOutProd.Id}",
                JsonOptions);
            Assert.NotNull(reservedOutDetails);
            Assert.False(reservedOutDetails.IsAvailable);

            var noInvDetails = await client.GetFromJsonAsync<CatalogProductDetailsDto>(
                $"/api/stores/{store.Slug}/catalog/products/{noInventoryProd.Id}",
                JsonOptions);
            Assert.NotNull(noInvDetails);
            Assert.False(noInvDetails.IsAvailable);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public void ProductCreation_EnforcesStoreSettingsCurrencyPrecisionAndTenantMatch()
    {
        var (jodStore, jodSettings) = CreateStoreWithSettings("JOD Store", "jod-s", StoreType.OnlineOnly, StoreStatus.Active, Jod);
        var (kwdStore, kwdSettings) = CreateStoreWithSettings("KWD Store", "kwd-s", StoreType.OnlineOnly, StoreStatus.Active, Kwd);
        var (usdStore, usdSettings) = CreateStoreWithSettings("USD Store", "usd-s", StoreType.OnlineOnly, StoreStatus.Active, Usd);
        var (eurStore, eurSettings) = CreateStoreWithSettings("EUR Store", "eur-s", StoreType.OnlineOnly, StoreStatus.Active, Eur);
        var (jpyStore, jpySettings) = CreateStoreWithSettings("JPY Store", "jpy-s", StoreType.OnlineOnly, StoreStatus.Active, Jpy);

        var catId = Guid.NewGuid();

        var jodProd = Product.Create(Guid.NewGuid(), jodStore.Id, catId, "JOD Item", 10.125m, jodSettings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);
        var kwdProd = Product.Create(Guid.NewGuid(), kwdStore.Id, catId, "KWD Item", 4.250m, kwdSettings, Kwd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var usdProd = Product.Create(Guid.NewGuid(), usdStore.Id, catId, "USD Item", 10.25m, usdSettings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var eurProd = Product.Create(Guid.NewGuid(), eurStore.Id, catId, "EUR Item", 9.99m, eurSettings, Eur, ProductStatus.Published, DateTimeOffset.UtcNow);
        var jpyProd = Product.Create(Guid.NewGuid(), jpyStore.Id, catId, "JPY Item", 1000m, jpySettings, Jpy, ProductStatus.Published, DateTimeOffset.UtcNow);

        Assert.Equal(10.125m, jodProd.Price);
        Assert.Equal(4.250m, kwdProd.Price);
        Assert.Equal(10.25m, usdProd.Price);
        Assert.Equal(9.99m, eurProd.Price);
        Assert.Equal(1000m, jpyProd.Price);

        // Excess decimal places rejected without rounding
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), jodStore.Id, catId, "Bad JOD", 10.1234m, jodSettings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), kwdStore.Id, catId, "Bad KWD", 4.2501m, kwdSettings, Kwd, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), usdStore.Id, catId, "Bad USD", 10.251m, usdSettings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), eurStore.Id, catId, "Bad EUR", 9.999m, eurSettings, Eur, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), jpyStore.Id, catId, "Bad JPY", 1000.5m, jpySettings, Jpy, ProductStatus.Published, DateTimeOffset.UtcNow));

        // USD store cannot create product using JOD precision/currency
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), usdStore.Id, catId, "Mismatch", 1.234m, usdSettings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow));

        // StoreSettings belonging to another Store cannot be used
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), usdStore.Id, catId, "Cross Settings", 10.125m, jodSettings, Jod, ProductStatus.Published, DateTimeOffset.UtcNow));

        // Negative price rejected
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create(Guid.NewGuid(), usdStore.Id, catId, "Neg", -0.01m, usdSettings, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task StoreSettings_PickupAllowedOnlyForPhysicalStores()
    {
        var suffix = NewSuffix();
        var physicalStore = Store.Create(Guid.NewGuid(), "Physical", $"phys-{suffix}", StoreType.Physical, StoreStatus.Active, DateTimeOffset.UtcNow);
        var onlineStore = Store.Create(Guid.NewGuid(), "Online", $"onl-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);

        // All 4 combinations:
        var physWithPickup = StoreSettings.Create(Guid.NewGuid(), physicalStore, Usd, "UTC", deliveryEnabled: true, pickupEnabled: true);
        var physWithoutPickup = StoreSettings.Create(Guid.NewGuid(), physicalStore, Usd, "UTC", deliveryEnabled: true, pickupEnabled: false);
        var onlineWithoutPickup = StoreSettings.Create(Guid.NewGuid(), onlineStore, Usd, "UTC", deliveryEnabled: true, pickupEnabled: false);

        Assert.True(physWithPickup.PickupEnabled);
        Assert.False(physWithoutPickup.PickupEnabled);
        Assert.False(onlineWithoutPickup.PickupEnabled);

        Assert.Throws<InvalidOperationException>(() =>
            StoreSettings.Create(Guid.NewGuid(), onlineStore, Usd, "UTC", deliveryEnabled: true, pickupEnabled: true));

        // UpdateFulfillmentOptions enforces ST-03 and StoreId match
        Assert.Throws<InvalidOperationException>(() =>
            onlineWithoutPickup.UpdateFulfillmentOptions(onlineStore, deliveryEnabled: true, pickupEnabled: true));
        Assert.Throws<ArgumentException>(() =>
            onlineWithoutPickup.UpdateFulfillmentOptions(physicalStore, deliveryEnabled: true, pickupEnabled: true));

        // Persistence round-trip succeeds for valid combinations
        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(physicalStore, onlineStore);
            db.StoreSettings.AddRange(physWithPickup, onlineWithoutPickup);
            await db.SaveChangesAsync();
        }

        await _fixture.DeleteStoresAsync(physicalStore.Id, onlineStore.Id);
    }

    [Fact]
    public async Task CurrencyAndStoreSettings_SqlConstraintsAndLifecycle_EnforceIntegrity()
    {
        await using (var db = _fixture.CreateDbContext())
        {
            var seededCodes = await db.Currencies.AsNoTracking().Select(c => c.Code).ToListAsync();
            Assert.Contains("JOD", seededCodes);
            Assert.Contains("USD", seededCodes);
            Assert.Contains("KWD", seededCodes);
            Assert.Contains("JPY", seededCodes);
        }

        var inactiveCurrency = Currency.Create("XTS", "963", 2, isActive: false);
        var dummyStore = Store.Create(Guid.NewGuid(), "Dummy", $"dum-{NewSuffix()}", StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow);
        Assert.Throws<InvalidOperationException>(() =>
            StoreSettings.Create(Guid.NewGuid(), dummyStore, inactiveCurrency, "UTC", true, false));

        var suffix = NewSuffix();
        var tempCurrencyCode = string.Concat(
            'Z',
            (char)('A' + (suffix[0] % 26)),
            (char)('A' + (suffix[1] % 26)));
        var tempNumericCode = "991";
        var tempCurrency = Currency.Create(tempCurrencyCode, tempNumericCode, 3, isActive: true);
        var (store, settings) = CreateStoreWithSettings("Temp Curr Store", $"tcurr-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, tempCurrency);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Currencies.Add(tempCurrency);
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        try
        {
            // Duplicate StoreSettings.StoreId rejected
            await using (var db = _fixture.CreateDbContext())
            {
                db.StoreSettings.Add(StoreSettings.Create(Guid.NewGuid(), store, Usd, "UTC", false, false));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Duplicate NumericCode rejected by UX_Currencies_NumericCode
            await using (var db = _fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO Currencies (Code, NumericCode, MinorUnit, IsActive) VALUES ('ZZZ', {tempNumericCode}, 2, 1)"));
            }

            // Lowercase Code rejected by CK_Currencies_Code_Format (binary collation)
            await using (var db = _fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO Currencies (Code, NumericCode, MinorUnit, IsActive) VALUES ('abc', '992', 2, 1)"));
            }

            // Deleting referenced currency rejected by FK_StoreSettings_Currencies_CurrencyCode
            await using (var db = _fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"DELETE FROM Currencies WHERE Code = {tempCurrencyCode}"));
            }

            // Invalid currency MinorUnit (>4) rejected by SQL CHECK constraint
            await using (var db = _fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO Currencies (Code, NumericCode, MinorUnit, IsActive) VALUES ('BAD', '993', 5, 1)"));
            }

            // Deactivating an existing store's currency blocks new selection, but existing store still resolves
            await using (var db = _fixture.CreateDbContext())
            {
                await db.Currencies
                    .Where(c => c.Code == tempCurrencyCode)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
            }

            using var client = _fixture.CreateHttpsClient();
            var catResp = await client.GetAsync($"/api/stores/{store.Slug}/catalog/categories");
            Assert.Equal(HttpStatusCode.OK, catResp.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
            await using var db = _fixture.CreateDbContext();
            await db.Currencies.Where(c => c.Code == tempCurrencyCode).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task DatabaseConstraints_EnforceTenantIsolationAndDomainInvariants()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Store A", $"const-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);
        var (storeB, settingsB) = CreateStoreWithSettings("Store B", $"const-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Eur);
        var catA = Category.Create(Guid.NewGuid(), storeA.Id, "Cat A", $"cat-a-{suffix}", true, 1);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "Cat B", $"cat-b-{suffix}", true, 1);
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Prod A", 10m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow, sku: $"SHARED-SKU-{suffix}");

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            db.Categories.AddRange(catA, catB);
            db.Products.Add(prodA);
            await db.SaveChangesAsync();
        }

        try
        {
            await using (var db = _fixture.CreateDbContext())
            {
                db.Stores.Add(Store.Create(Guid.NewGuid(), "Dup", storeA.Slug, StoreType.OnlineOnly, StoreStatus.Active, DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Dup Sku", 12m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow, sku: prodA.Sku));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Store B Same Sku", 12m, settingsB, Eur, ProductStatus.Published, DateTimeOffset.UtcNow, sku: prodA.Sku));
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Null Sku 1", 12m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Null Sku 2", 12m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }

            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catB.Id, "Cross Cat", 10m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            await using (var db = _fixture.CreateDbContext())
            {
                db.InventoryItems.Add(InventoryItem.Create(Guid.NewGuid(), storeB.Id, prodA.Id, 5, 0));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            await using (var db = _fixture.CreateDbContext())
            {
                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO InventoryItems (Id, StoreId, ProductId, OnHand, Reserved) VALUES ({Guid.NewGuid()}, {storeA.Id}, {prodA.Id}, -1, 0)"));

                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO InventoryItems (Id, StoreId, ProductId, OnHand, Reserved) VALUES ({Guid.NewGuid()}, {storeA.Id}, {prodA.Id}, 5, -1)"));

                await Assert.ThrowsAsync<SqlException>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync(
                        $"INSERT INTO InventoryItems (Id, StoreId, ProductId, OnHand, Reserved) VALUES ({Guid.NewGuid()}, {storeA.Id}, {prodA.Id}, 5, 6)"));
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Theory]
    [InlineData("page=0", "page")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=51", "pageSize")]
    [InlineData("sort=invalid_sort", "sort")]
    public async Task InvalidQueryParameters_Return400ValidationProblemDetails(
        string queryString,
        string expectedKey)
    {
        var suffix = NewSuffix();
        var (store, settings) = CreateStoreWithSettings("Val Store", $"val-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();
            using var response = await client.GetAsync($"/api/stores/{store.Slug}/catalog/products?{queryString}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
            Assert.NotNull(problem);
            Assert.Equal(400, problem.Status);
            Assert.True(problem.Errors.ContainsKey(expectedKey));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task SearchOverMaxLength_Returns400ValidationProblemDetails()
    {
        var suffix = NewSuffix();
        var (store, settings) = CreateStoreWithSettings("Val Store", $"val-len-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();
            var longSearch = new string('a', CatalogProductsQuery.MaxSearchLength + 1);
            using var response = await client.GetAsync($"/api/stores/{store.Slug}/catalog/products?search={longSearch}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(JsonOptions);
            Assert.NotNull(problem);
            Assert.True(problem.Errors.ContainsKey("search"));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task ConcurrentRequests_ForDifferentStoreSlugs_RemainStrictlyIsolated()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Store A", $"conc-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Usd);
        var (storeB, settingsB) = CreateStoreWithSettings("Store B", $"conc-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Kwd);
        var catA = Category.Create(Guid.NewGuid(), storeA.Id, "Cat A", $"conc-cat-a-{suffix}", true, 1);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "Cat B", $"conc-cat-b-{suffix}", true, 1);
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Product A", 12.50m, settingsA, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var prodB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Product B", 4.250m, settingsB, Kwd, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            db.Categories.AddRange(catA, catB);
            db.Products.AddRange(prodA, prodB);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            var tasks = Enumerable.Range(0, 10).Select(async index =>
            {
                var isA = index % 2 == 0;
                var slug = isA ? storeA.Slug : storeB.Slug;
                var expectedId = isA ? prodA.Id : prodB.Id;
                var expectedCurrency = isA ? "USD" : "KWD";
                var expectedPrice = isA ? 12.50m : 4.250m;

                var payload = await client.GetFromJsonAsync<CatalogProductsResponse>(
                    $"/api/stores/{slug}/catalog/products",
                    JsonOptions);
                Assert.NotNull(payload);
                var item = Assert.Single(payload.Items);
                Assert.Equal(expectedId, item.Id);
                Assert.Equal(expectedCurrency, item.CurrencyCode);
                Assert.Equal(expectedPrice, item.Price);
            });

            await Task.WhenAll(tasks);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task ProductionHost_CatalogErrorsDoNotLeakInternalDetails()
    {
        await using var prodFactory = _fixture.CreateProductionFactory();
        using var client = prodFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://retail.example.com"),
            AllowAutoRedirect = false
        });

        using var notFoundResp = await client.GetAsync($"/api/stores/missing-{NewSuffix()}/catalog/products");
        var notFoundBody = await notFoundResp.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.NotFound, notFoundResp.StatusCode);
        Assert.DoesNotContain("Exception", notFoundBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SqlConnection", notFoundBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RetailFlowProduct", notFoundBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HealthEndpoints_LiveIsDbIndependent_AndReadyUsesOverrideEvenWithCiEnvVarPresent()
    {
        // Healthy DB -> /health/live 200, /health/ready 200
        using (var client = _fixture.CreateHttpsClient())
        {
            using var liveOk = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, liveOk.StatusCode);
            Assert.Equal("Healthy", await liveOk.Content.ReadAsStringAsync());

            using var readyOk = await client.GetAsync("/health/ready");
            Assert.Equal(HttpStatusCode.OK, readyOk.StatusCode);
            Assert.Equal("Healthy", await readyOk.Content.ReadAsStringAsync());
        }

        // Simulate CI environment where RETAILFLOW_CONNECTION is set in process environment,
        // and prove the test's IConfiguration override still targets the unreachable DB.
        const string unreachableConn = "Server=127.0.0.1,14399;Database=RetailFlowUnreachable;User Id=sa;Password=Invalid_123!;Connect Timeout=1;Encrypt=False";
        var originalEnv = Environment.GetEnvironmentVariable("RETAILFLOW_CONNECTION");
        try
        {
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", _fixture.ConnectionString);

            await using var unreachableFactory = _fixture.CreateProductionFactory(unreachableConn);
            using var unreachableClient = unreachableFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://retail.example.com"),
                AllowAutoRedirect = false
            });

            using var liveStillOk = await unreachableClient.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, liveStillOk.StatusCode);

            using var readyFailed = await unreachableClient.GetAsync("/health/ready");
            var readyBody = await readyFailed.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.ServiceUnavailable, readyFailed.StatusCode);
            Assert.Equal("Unhealthy", readyBody);
            Assert.DoesNotContain("14399", readyBody, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", readyBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ConnectionStrings", readyBody, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", originalEnv);
        }
    }

    [Fact]
    public async Task DatabaseReadinessHealthCheck_PropagatesCancellation()
    {
        await using var db = _fixture.CreateDbContext();
        var check = new DatabaseReadinessHealthCheck(db);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            check.CheckHealthAsync(new HealthCheckContext(), cts.Token));
    }

    [Fact]
    public void ConnectionStringResolution_EnforcesConfigurationPriorityAndFailsFastOutsideDevelopment()
    {
        // 1. Missing both in Production -> fail fast without leaking secrets
        var prodMissingConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "retail.example.com" })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            ConnectionStringResolver.ResolveRuntime(prodMissingConfig, isDevelopment: false));
        Assert.DoesNotContain("Server=.", ex.Message, StringComparison.Ordinal);

        // 2. Whitespace RETAILFLOW_CONNECTION falls back to ConnectionStrings:RetailFlow
        var whitespacePrimary = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RETAILFLOW_CONNECTION"] = "   ",
                ["ConnectionStrings:RetailFlow"] = "Server=cfg-host;Database=RetailFlowProduct;"
            })
            .Build();
        Assert.Equal(
            "Server=cfg-host;Database=RetailFlowProduct;",
            ConnectionStringResolver.ResolveRuntime(whitespacePrimary, isDevelopment: false));

        // 3. RETAILFLOW_CONNECTION absent uses ConnectionStrings:RetailFlow
        var absentPrimary = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:RetailFlow"] = "Server=cfg-host;Database=RetailFlowProduct;"
            })
            .Build();
        Assert.Equal(
            "Server=cfg-host;Database=RetailFlowProduct;",
            ConnectionStringResolver.ResolveRuntime(absentPrimary, isDevelopment: false));

        // 4. Non-empty RETAILFLOW_CONNECTION in IConfiguration overrides ConnectionStrings:RetailFlow
        var bothPresent = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RETAILFLOW_CONNECTION"] = "Server=primary-host;Database=RetailFlowProduct;",
                ["ConnectionStrings:RetailFlow"] = "Server=cfg-host;Database=RetailFlowProduct;"
            })
            .Build();
        Assert.Equal(
            "Server=primary-host;Database=RetailFlowProduct;",
            ConnectionStringResolver.ResolveRuntime(bothPresent, isDevelopment: false));

        // 5. Missing both in Development -> local fallback works
        var emptyConfig = new ConfigurationBuilder().Build();
        var devConn = ConnectionStringResolver.ResolveRuntime(emptyConfig, isDevelopment: true);
        Assert.Equal(ConnectionStringResolver.DefaultLocalConnectionString, devConn);
    }

    private static (Store Store, StoreSettings Settings) CreateStoreWithSettings(
        string name,
        string slug,
        StoreType storeType,
        StoreStatus status,
        Currency currency)
    {
        var store = Store.Create(
            Guid.NewGuid(),
            name,
            slug,
            storeType,
            status,
            DateTimeOffset.UtcNow);

        var settings = StoreSettings.Create(
            Guid.NewGuid(),
            store,
            currency,
            "UTC",
            deliveryEnabled: true,
            pickupEnabled: storeType == StoreType.Physical);

        return (store, settings);
    }

    [Fact]
    public async Task PublicStoreIdentity_ReturnsBrandingAndPublicAddress_AndNeverExposesFulfillmentAddress()
    {
        var suffix = NewSuffix();
        var physicalStore = Store.Create(
            Guid.NewGuid(),
            "Amman Roastery",
            $"roastery-{suffix}",
            StoreType.Physical,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);
        physicalStore.UpdateBranding(
            "Amman Roastery",
            "https://cdn.example/logo.png",
            "#1E293B",
            "hello@roastery.example",
            "+962790000001");
        var physicalSettings = StoreSettings.Create(
            Guid.NewGuid(),
            physicalStore,
            Jod,
            "Asia/Amman",
            deliveryEnabled: true,
            pickupEnabled: true,
            publicAddress: "Rainbow St 42, Amman",
            fulfillmentAddress: "INTERNAL-WAREHOUSE-BAY-9");

        var onlineStore = Store.Create(
            Guid.NewGuid(),
            "Cloud Roastery",
            $"cloud-{suffix}",
            StoreType.OnlineOnly,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);
        var onlineSettings = StoreSettings.Create(
            Guid.NewGuid(),
            onlineStore,
            Usd,
            "UTC",
            deliveryEnabled: true,
            pickupEnabled: false,
            publicAddress: null,
            fulfillmentAddress: "SECRET-FULFILLMENT-DOCK-3");

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(physicalStore, onlineStore);
            db.StoreSettings.AddRange(physicalSettings, onlineSettings);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            using var physResponse = await client.GetAsync($"/api/stores/{physicalStore.Slug}");
            Assert.Equal(HttpStatusCode.OK, physResponse.StatusCode);
            var physRaw = await physResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("INTERNAL-WAREHOUSE-BAY-9", physRaw, StringComparison.Ordinal);
            Assert.DoesNotContain("fulfillmentAddress", physRaw, StringComparison.OrdinalIgnoreCase);

            var physDto = System.Text.Json.JsonSerializer.Deserialize<PublicStoreIdentityDto>(physRaw, JsonOptions);
            Assert.NotNull(physDto);
            Assert.Equal("Amman Roastery", physDto.StoreName);
            Assert.Equal("JOD", physDto.CurrencyCode);
            Assert.Equal("https://cdn.example/logo.png", physDto.LogoUrl);
            Assert.Equal("#1E293B", physDto.PrimaryColor);
            Assert.True(physDto.PickupEnabled);
            Assert.Equal("Rainbow St 42, Amman", physDto.PublicAddress);

            using var onlineResponse = await client.GetAsync($"/api/stores/{onlineStore.Slug}");
            Assert.Equal(HttpStatusCode.OK, onlineResponse.StatusCode);
            var onlineRaw = await onlineResponse.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SECRET-FULFILLMENT-DOCK-3", onlineRaw, StringComparison.Ordinal);

            var onlineDto = System.Text.Json.JsonSerializer.Deserialize<PublicStoreIdentityDto>(onlineRaw, JsonOptions);
            Assert.NotNull(onlineDto);
            Assert.False(onlineDto.PickupEnabled);
            Assert.Null(onlineDto.PublicAddress);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(physicalStore.Id, onlineStore.Id);
        }
    }

    [Fact]
    public async Task PublicStoreIdentity_UnknownOrNonActiveStore_Returns404ProblemDetails()
    {
        var suffix = NewSuffix();
        var (pendingStore, pendingSettings) = CreateStoreWithSettings(
            "Pending Shop",
            $"pending-id-{suffix}",
            StoreType.Physical,
            StoreStatus.PendingApproval,
            Usd);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(pendingStore);
            db.StoreSettings.Add(pendingSettings);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            using var pendingResp = await client.GetAsync($"/api/stores/{pendingStore.Slug}");
            Assert.Equal(HttpStatusCode.NotFound, pendingResp.StatusCode);
            Assert.Equal("application/problem+json", pendingResp.Content.Headers.ContentType?.MediaType);

            using var unknownResp = await client.GetAsync($"/api/stores/does-not-exist-{suffix}");
            Assert.Equal(HttpStatusCode.NotFound, unknownResp.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(pendingStore.Id);
        }
    }

    [Fact]
    public async Task DeliveryZones_SqlConstraints_EnforceUniqueNamePerStoreAndNonNegativeFee()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Zone A", $"zone-a-{suffix}", StoreType.Physical, StoreStatus.Active, Jod);
        var (storeB, settingsB) = CreateStoreWithSettings("Zone B", $"zone-b-{suffix}", StoreType.Physical, StoreStatus.Active, Jod);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            await db.SaveChangesAsync();
        }

        try
        {
            await using (var db = _fixture.CreateDbContext())
            {
                db.DeliveryZones.Add(DeliveryZone.Create(Guid.NewGuid(), storeA.Id, "West Amman", 2.500m, settingsA, Jod, DateTimeOffset.UtcNow));
                db.DeliveryZones.Add(DeliveryZone.Create(Guid.NewGuid(), storeB.Id, "West Amman", 3.000m, settingsB, Jod, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }

            // Duplicate normalized name in same store fails UX_DeliveryZones_StoreId_NormalizedName
            await using (var db = _fixture.CreateDbContext())
            {
                db.DeliveryZones.Add(DeliveryZone.Create(Guid.NewGuid(), storeA.Id, "west amman", 2.000m, settingsA, Jod, DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // Negative fee fails CK_DeliveryZones_FlatFee_NonNegative
            await using (var db = _fixture.CreateDbContext())
            {
                var ex = await Assert.ThrowsAnyAsync<Exception>(() =>
                    db.Database.ExecuteSqlInterpolatedAsync($"""
                        INSERT INTO [DeliveryZones] ([Id], [StoreId], [Name], [NormalizedName], [FlatFee], [IsEnabled], [CreatedAtUtc])
                        VALUES ({Guid.NewGuid()}, {storeA.Id}, 'Neg', 'NEG', -1.0000, 1, SYSUTCDATETIME())
                        """));
                Assert.NotNull(ex);
            }
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task PublicStoreDeliveryZones_ReturnsOnlyEnabledZonesForTargetStore()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Delivery A", $"deliv-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Jod);
        var (storeB, settingsB) = CreateStoreWithSettings("Delivery B", $"deliv-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, Jod);
        var storeDisabled = Store.Create(
            Guid.NewGuid(),
            "Delivery Off",
            $"deliv-off-{suffix}",
            StoreType.OnlineOnly,
            StoreStatus.Active,
            DateTimeOffset.UtcNow);
        var settingsDisabled = StoreSettings.Create(
            Guid.NewGuid(),
            storeDisabled,
            Jod,
            "Asia/Amman",
            deliveryEnabled: false,
            pickupEnabled: false);

        var enabledZoneZ = DeliveryZone.Create(Guid.NewGuid(), storeA.Id, "Zarqa", 3.500m, settingsA, Jod, DateTimeOffset.UtcNow);
        var enabledZoneA = DeliveryZone.Create(Guid.NewGuid(), storeA.Id, "Abdoun", 2.000m, settingsA, Jod, DateTimeOffset.UtcNow);
        var disabledZone = DeliveryZone.Create(Guid.NewGuid(), storeA.Id, "Irbid", 5.000m, settingsA, Jod, DateTimeOffset.UtcNow);
        disabledZone.SetEnabled(false);

        var otherStoreZone = DeliveryZone.Create(Guid.NewGuid(), storeB.Id, "Other Store Zone", 1.500m, settingsB, Jod, DateTimeOffset.UtcNow);
        var disabledStoreModeZone = DeliveryZone.Create(Guid.NewGuid(), storeDisabled.Id, "Disabled Mode Zone", 2.000m, settingsDisabled, Jod, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB, storeDisabled);
            db.StoreSettings.AddRange(settingsA, settingsB, settingsDisabled);
            db.DeliveryZones.AddRange(enabledZoneZ, enabledZoneA, disabledZone, otherStoreZone, disabledStoreModeZone);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            var response = await client.GetAsync($"/api/stores/{storeA.Slug}/delivery-zones");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<PublicDeliveryZonesResponse>(JsonOptions);
            Assert.NotNull(payload);
            Assert.True(payload!.DeliveryEnabled);
            Assert.Equal("JOD", payload.CurrencyCode);
            Assert.Collection(
                payload.Zones,
                first =>
                {
                    Assert.Equal(enabledZoneA.Id, first.Id);
                    Assert.Equal("Abdoun", first.Name);
                    Assert.Equal(2.000m, first.FlatFee);
                },
                second =>
                {
                    Assert.Equal(enabledZoneZ.Id, second.Id);
                    Assert.Equal("Zarqa", second.Name);
                    Assert.Equal(3.500m, second.FlatFee);
                });

            var disabledModeResponse = await client.GetAsync($"/api/stores/{storeDisabled.Slug}/delivery-zones");
            Assert.Equal(HttpStatusCode.OK, disabledModeResponse.StatusCode);

            var disabledModePayload = await disabledModeResponse.Content.ReadFromJsonAsync<PublicDeliveryZonesResponse>(JsonOptions);
            Assert.NotNull(disabledModePayload);
            Assert.False(disabledModePayload!.DeliveryEnabled);
            Assert.Empty(disabledModePayload.Zones);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id, storeDisabled.Id);
        }
    }

    [Fact]
    public async Task MerchantServices_EnforceTenantIsolationAndUpdateCatalogAndDeliveryZones()
    {
        var suffix = NewSuffix();
        var (storeA, settingsA) = CreateStoreWithSettings("Merch A", $"merch-a-{suffix}", StoreType.Physical, StoreStatus.Active, Jod);
        var (storeB, settingsB) = CreateStoreWithSettings("Merch B", $"merch-b-{suffix}", StoreType.Physical, StoreStatus.Active, Jod);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            await db.SaveChangesAsync();
        }

        try
        {
            await using var scope = _fixture.CreateAsyncScope();
            var storeService = scope.ServiceProvider.GetRequiredService<IMerchantStoreService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<IMerchantCatalogService>();

            Assert.True(await storeService.UpdateBrandingAsync(
                storeA.Id,
                new UpdateStoreBrandingCommand("Merch A Updated", "https://cdn.example/a.png", "#0F172A", "a@example.com", "+962790000001"),
                CancellationToken.None));

            Assert.True(await storeService.UpdateAddressesAsync(
                storeA.Id,
                new UpdateStoreAddressesCommand("Al-Weibdeh, Amman", "Warehouse 12"),
                CancellationToken.None));

            var zone = await storeService.CreateDeliveryZoneAsync(
                storeA.Id,
                new CreateDeliveryZoneCommand("Shmeisani", 2.750m, true),
                CancellationToken.None);

            // Cross-tenant storeB cannot mutate storeA's zone
            var crossTenantZoneUpdate = await storeService.UpdateDeliveryZoneAsync(
                storeB.Id,
                zone.Id,
                new UpdateDeliveryZoneCommand(1.000m, false),
                CancellationToken.None);
            Assert.Null(crossTenantZoneUpdate);

            var category = await catalogService.CreateCategoryAsync(
                storeA.Id,
                new CreateCategoryCommand("Roasts", "roasts", 1, true),
                CancellationToken.None);

            var product = await catalogService.CreateProductAsync(
                storeA.Id,
                new CreateProductCommand(category.Id, "House Blend", "Smooth roast", $"HB-{suffix}", 9.500m, ProductStatus.Draft, 12),
                CancellationToken.None);
            Assert.Equal(ProductStatus.Draft, product.Status);
            Assert.Equal(12, product.OnHand);

            // Cross-tenant storeB cannot publish or update storeA's product or stock
            Assert.False(await catalogService.PublishProductAsync(storeB.Id, product.Id, CancellationToken.None));
            Assert.False(await catalogService.SetInventoryOnHandAsync(storeB.Id, product.Id, 0, CancellationToken.None));

            Assert.True(await catalogService.PublishProductAsync(storeA.Id, product.Id, CancellationToken.None));
            Assert.True(await catalogService.SetInventoryOnHandAsync(storeA.Id, product.Id, 20, CancellationToken.None));

            using var client = _fixture.CreateHttpsClient();
            var catalogResponse = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/products");
            Assert.Equal(HttpStatusCode.OK, catalogResponse.StatusCode);
            var catalogPayload = await catalogResponse.Content.ReadFromJsonAsync<CatalogProductsResponse>(JsonOptions);
            Assert.NotNull(catalogPayload);
            var published = Assert.Single(catalogPayload!.Items);
            Assert.Equal(product.Id, published.Id);
            Assert.True(published.IsAvailable);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task OpenApiDocument_InDevelopment_IncludesPublicEndpointsAndProblemResponses()
    {
        using var client = _fixture.CreateHttpsClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var openApiJson = await response.Content.ReadAsStringAsync();
        Assert.Contains("/api/stores/{slug}", openApiJson, StringComparison.Ordinal);
        Assert.Contains("/api/stores/{slug}/delivery-zones", openApiJson, StringComparison.Ordinal);
        Assert.Contains("/api/stores/{slug}/catalog/categories", openApiJson, StringComparison.Ordinal);
        Assert.Contains("/api/stores/{slug}/catalog/products", openApiJson, StringComparison.Ordinal);
        Assert.Contains("/api/stores/{slug}/catalog/products/{productId}", openApiJson, StringComparison.Ordinal);
        Assert.Contains("\"404\"", openApiJson, StringComparison.Ordinal);
        Assert.Contains("\"400\"", openApiJson, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", openApiJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PlatformStoreLifecycle_AndProductImages_EnforceVisibilityAndTenantIsolation()
    {
        var suffix = NewSuffix();
        Guid createdStoreId = Guid.Empty;

        try
        {
            await using var scope = _fixture.CreateAsyncScope();
            var platformService = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
            var catalogService = scope.ServiceProvider.GetRequiredService<IMerchantCatalogService>();

            var created = await platformService.CreateStoreAsync(
                new CreateStoreCommand(
                    "Lifecycle Roastery",
                    $"lifecycle-{suffix}",
                    StoreType.Physical,
                    "JOD",
                    "Asia/Amman",
                    DeliveryEnabled: true,
                    PickupEnabled: true,
                    PublicAddress: "Amman Blvd",
                    FulfillmentAddress: "Dock 7"),
                CancellationToken.None);
            createdStoreId = created.Id;

            using var client = _fixture.CreateHttpsClient();

            // PendingApproval store is not publicly accessible
            var pendingResponse = await client.GetAsync($"/api/stores/{created.Slug}");
            Assert.Equal(HttpStatusCode.NotFound, pendingResponse.StatusCode);

            // Approve makes store publicly accessible
            Assert.True(await platformService.ApproveStoreAsync(created.Id, CancellationToken.None));
            var activeResponse = await client.GetAsync($"/api/stores/{created.Slug}");
            Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);

            // Add category, published product, and ordered gallery images
            var category = await catalogService.CreateCategoryAsync(
                created.Id,
                new CreateCategoryCommand("Beans", "beans", 1, true),
                CancellationToken.None);
            var product = await catalogService.CreateProductAsync(
                created.Id,
                new CreateProductCommand(category.Id, "Single Origin", "Floral", $"SO-{suffix}", 14.000m, ProductStatus.Published, 5),
                CancellationToken.None);

            var img2 = await catalogService.AddProductImageAsync(
                created.Id,
                product.Id,
                new AddProductImageCommand("https://cdn.example/2.png", SortOrder: 2, AltText: "Second"),
                CancellationToken.None);
            var img1 = await catalogService.AddProductImageAsync(
                created.Id,
                product.Id,
                new AddProductImageCommand("https://cdn.example/1.png", SortOrder: 1, AltText: "First"),
                CancellationToken.None);
            Assert.NotNull(img1);
            Assert.NotNull(img2);

            // Cross-tenant image add/remove is rejected
            Assert.Null(await catalogService.AddProductImageAsync(
                Guid.NewGuid(),
                product.Id,
                new AddProductImageCommand("https://cdn.example/x.png"),
                CancellationToken.None));
            Assert.False(await catalogService.RemoveProductImageAsync(
                Guid.NewGuid(),
                product.Id,
                img1!.Id,
                CancellationToken.None));

            var details = await client.GetFromJsonAsync<CatalogProductDetailsDto>(
                $"/api/stores/{created.Slug}/catalog/products/{product.Id}",
                JsonOptions);
            Assert.NotNull(details);
            Assert.Collection(
                details!.Images,
                first => Assert.Equal("https://cdn.example/1.png", first.ImageUrl),
                second => Assert.Equal("https://cdn.example/2.png", second.ImageUrl));

            Assert.True(await catalogService.RemoveProductImageAsync(created.Id, product.Id, img2!.Id, CancellationToken.None));

            // Suspend hides store publicly again
            Assert.True(await platformService.SuspendStoreAsync(created.Id, CancellationToken.None));
            var suspendedResponse = await client.GetAsync($"/api/stores/{created.Slug}");
            Assert.Equal(HttpStatusCode.NotFound, suspendedResponse.StatusCode);
        }
        finally
        {
            if (createdStoreId != Guid.Empty)
            {
                await _fixture.DeleteStoresAsync(createdStoreId);
            }
        }
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];
}

public sealed class CatalogSqlTestFixture : IAsyncLifetime
{
    private const string DefaultLocalConnectionString =
        "Server=.;Database=RetailFlowProduct;Trusted_Connection=True;TrustServerCertificate=True";

    private WebApplicationFactory<Program>? _factory;

    public string ConnectionString { get; } = ResolveConnectionString();

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "localhost",
                        ["RETAILFLOW_CONNECTION"] = ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = ConnectionString
                    });
                });
            });

        await using var db = CreateDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    public HttpClient CreateHttpsClient()
    {
        var factory = _factory ?? throw new InvalidOperationException("Fixture is not initialized.");
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    public WebApplicationFactory<Program> CreateProductionFactory(string? connectionStringOverride = null)
    {
        var effectiveConnection = connectionStringOverride ?? ConnectionString;
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["RETAILFLOW_CONNECTION"] = effectiveConnection,
                        ["ConnectionStrings:RetailFlow"] = effectiveConnection
                    });
                });
            });
    }

    public Microsoft.Extensions.DependencyInjection.AsyncServiceScope CreateAsyncScope()
    {
        var factory = _factory ?? throw new InvalidOperationException("Fixture is not initialized.");
        return factory.Services.CreateAsyncScope();
    }

    public RetailFlowDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<RetailFlowDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;
        return new RetailFlowDbContext(options);
    }

    public async Task DeleteStoresAsync(params Guid[] storeIds)
    {
        if (storeIds.Length == 0)
        {
            return;
        }

        await using var db = CreateDbContext();
        await db.OrderStatusHistories.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.OrderItems.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Orders.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.CartItems.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Carts.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.CheckoutRequests.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.SessionTickets.Where(x => x.StoreId.HasValue && storeIds.Contains(x.StoreId.Value)).ExecuteDeleteAsync();
        await db.Users.Where(x => x.StoreId.HasValue && storeIds.Contains(x.StoreId.Value)).ExecuteDeleteAsync();
        await db.ProductImages.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.InventoryItems.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Products.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Categories.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.DeliveryZones.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.StoreSettings.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Stores.Where(x => storeIds.Contains(x.Id)).ExecuteDeleteAsync();
    }

    private static string ResolveConnectionString()
    {
        var fromEnv = Environment.GetEnvironmentVariable("RETAILFLOW_CONNECTION");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        var fromAspNetEnv = Environment.GetEnvironmentVariable("ConnectionStrings__RetailFlow");
        if (!string.IsNullOrWhiteSpace(fromAspNetEnv))
        {
            return fromAspNetEnv;
        }

        return DefaultLocalConnectionString;
    }
}
