using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using RetailFlow.Application.Catalog;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Currencies;
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
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Wireless Mouse", 29.99m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow, description: "Ergonomic wireless mouse", sku: $"SKU-{suffix}-1");
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
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Prod", 10m, Sar, ProductStatus.Published, DateTimeOffset.UtcNow);

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

        var publishedA = Product.Create(Guid.NewGuid(), storeA.Id, catActiveA.Id, "Published A", 15m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var publishedInInactiveCatA = Product.Create(Guid.NewGuid(), storeA.Id, catInactiveA.Id, "Hidden Cat Product", 15m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var draftA = Product.Create(Guid.NewGuid(), storeA.Id, catActiveA.Id, "Draft A", 15m, Usd, ProductStatus.Draft, DateTimeOffset.UtcNow);
        var archivedA = Product.Create(Guid.NewGuid(), storeA.Id, catActiveA.Id, "Archived A", 15m, Usd, ProductStatus.Archived, DateTimeOffset.UtcNow);
        var publishedB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Published B", 15m, Eur, ProductStatus.Published, DateTimeOffset.UtcNow);

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

            // Unfiltered list hides Draft, Archived, Inactive-category, and Store B products
            var listPayload = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{storeA.Slug}/catalog/products",
                JsonOptions);
            Assert.NotNull(listPayload);
            var single = Assert.Single(listPayload.Items);
            Assert.Equal(publishedA.Id, single.Id);

            // Details endpoint returns 404 for all hidden products including inactive category
            foreach (var hiddenId in new[] { publishedInInactiveCatA.Id, draftA.Id, archivedA.Id, publishedB.Id })
            {
                using var detailResp = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/products/{hiddenId}");
                Assert.Equal(HttpStatusCode.NotFound, detailResp.StatusCode);
            }

            // Category filters cannot bypass IsActive
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

            // Reactivating the category makes the published product visible in list and details
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
        var p1 = Product.Create(Guid.NewGuid(), store.Id, catBooks.Id, "Alpha Book", 30m, Eur, ProductStatus.Published, t0, sku: $"BK-{suffix}-01");
        var p2 = Product.Create(Guid.NewGuid(), store.Id, catBooks.Id, "Gamma Guide", 10m, Eur, ProductStatus.Published, t0.AddMinutes(1), sku: $"BK-{suffix}-02");
        var p3 = Product.Create(Guid.NewGuid(), store.Id, catGear.Id, "Beta Lamp", 20m, Eur, ProductStatus.Published, t0.AddMinutes(2), sku: $"GR-{suffix}-03");
        var p4 = Product.Create(Guid.NewGuid(), store.Id, catGear.Id, "Delta Mug", 5m, Eur, ProductStatus.Published, t0.AddMinutes(3), sku: $"GR-{suffix}-04");

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

            // Search by SKU (case-insensitive)
            var skuResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?search=gr-{suffix}-03",
                JsonOptions);
            Assert.NotNull(skuResult);
            Assert.Equal([p3.Id], skuResult.Items.Select(i => i.Id).ToArray());

            // Filter by categoryId
            var catIdResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catBooks.Id}&sort=price_asc",
                JsonOptions);
            Assert.NotNull(catIdResult);
            Assert.Equal([p2.Id, p1.Id], catIdResult.Items.Select(i => i.Id).ToArray());

            // Matching categoryId + categorySlug vs conflicting categoryId + categorySlug
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

            // Sort price_desc + pagination
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

            // Out-of-range page returns empty items
            var page3 = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?sort=name_asc&page=3&pageSize=2",
                JsonOptions);
            Assert.NotNull(page3);
            Assert.Empty(page3.Items);
            Assert.Equal(4, page3.TotalCount);

            // Large page value does not overflow
            var maxPage = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?page={int.MaxValue}&pageSize=50",
                JsonOptions);
            Assert.NotNull(maxPage);
            Assert.Equal(int.MaxValue, maxPage.Page);
            Assert.Empty(maxPage.Items);
            Assert.Equal(4, maxPage.TotalCount);

            // Sort name_desc
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

        var availableProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Available", 10m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var reservedOutProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "ReservedOut", 10m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var noInventoryProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "NoInventory", 10m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);

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
    public void MoneyPrecision_ValidatesAgainstCurrencyMinorUnit_WithoutImplicitRounding()
    {
        var categoryId = Guid.NewGuid();
        var storeId = Guid.NewGuid();

        // Valid amounts
        var jodProd = Product.Create(Guid.NewGuid(), storeId, categoryId, "JOD Item", 1.250m, Jod, ProductStatus.Published, DateTimeOffset.UtcNow);
        var kwdProd = Product.Create(Guid.NewGuid(), storeId, categoryId, "KWD Item", 4.250m, Kwd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var usdProd = Product.Create(Guid.NewGuid(), storeId, categoryId, "USD Item", 12.50m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var eurProd = Product.Create(Guid.NewGuid(), storeId, categoryId, "EUR Item", 9.99m, Eur, ProductStatus.Published, DateTimeOffset.UtcNow);
        var jpyProd = Product.Create(Guid.NewGuid(), storeId, categoryId, "JPY Item", 1000m, Jpy, ProductStatus.Published, DateTimeOffset.UtcNow);

        Assert.Equal(1.250m, jodProd.Price);
        Assert.Equal(4.250m, kwdProd.Price);
        Assert.Equal(12.50m, usdProd.Price);
        Assert.Equal(9.99m, eurProd.Price);
        Assert.Equal(1000m, jpyProd.Price);

        // Excess decimal places rejected without rounding
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), storeId, categoryId, "Bad JOD", 1.2505m, Jod, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), storeId, categoryId, "Bad KWD", 4.2501m, Kwd, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), storeId, categoryId, "Bad USD", 12.501m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), storeId, categoryId, "Bad EUR", 9.999m, Eur, ProductStatus.Published, DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => Product.Create(Guid.NewGuid(), storeId, categoryId, "Bad JPY", 1000.5m, Jpy, ProductStatus.Published, DateTimeOffset.UtcNow));

        // Negative price rejected
        Assert.Throws<ArgumentOutOfRangeException>(() => Product.Create(Guid.NewGuid(), storeId, categoryId, "Neg", -0.01m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
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

        // Inactive currency cannot be assigned to new StoreSettings
        var inactiveCurrency = Currency.Create("XTS", "963", 2, isActive: false);
        Assert.Throws<InvalidOperationException>(() =>
            StoreSettings.Create(Guid.NewGuid(), Guid.NewGuid(), inactiveCurrency, "UTC", true, false));

        var suffix = NewSuffix();
        var tempCurrencyCode = string.Concat(
            'Z',
            (char)('A' + (suffix[0] % 26)),
            (char)('A' + (suffix[1] % 26)));
        var tempCurrency = Currency.Create(tempCurrencyCode, "991", 3, isActive: true);
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
                db.StoreSettings.Add(StoreSettings.Create(Guid.NewGuid(), store.Id, Usd, "UTC", false, true));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
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
                        $"INSERT INTO Currencies (Code, NumericCode, MinorUnit, IsActive) VALUES ('BAD', '999', 5, 1)"));
            }

            // Deactivating an existing store's currency prevents new selection, but existing store still resolves
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
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Prod A", 10m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow, sku: $"SHARED-SKU-{suffix}");

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
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Dup Sku", 12m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow, sku: prodA.Sku));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Store B Same Sku", 12m, Eur, ProductStatus.Published, DateTimeOffset.UtcNow, sku: prodA.Sku));
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Null Sku 1", 12m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Null Sku 2", 12m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }

            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catB.Id, "Cross Cat", 10m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow));
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
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Product A", 12.50m, Usd, ProductStatus.Published, DateTimeOffset.UtcNow);
        var prodB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Product B", 4.250m, Kwd, ProductStatus.Published, DateTimeOffset.UtcNow);

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
    public async Task HealthEndpoints_LiveIsDbIndependent_AndReadyReflectsSqlConnectivity()
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

        // Unreachable DB -> /health/live still 200, /health/ready returns 503 Unhealthy without leaking connection string
        const string unreachableConn = "Server=127.0.0.1,14399;Database=RetailFlowUnreachable;User Id=sa;Password=Invalid_123!;Connect Timeout=1;Encrypt=False";
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

    [Fact]
    public void ConnectionStringResolution_EnforcesPriorityAndFailsFastOutsideDevelopment()
    {
        var originalEnv = Environment.GetEnvironmentVariable("RETAILFLOW_CONNECTION");
        try
        {
            // 1. Missing both in Production -> fail fast without leaking secrets
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", null);
            var prodMissingConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AllowedHosts"] = "retail.example.com" })
                .Build();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ConnectionStringResolver.ResolveRuntime(prodMissingConfig, isDevelopment: false));
            Assert.DoesNotContain("Server=.", ex.Message, StringComparison.Ordinal);

            // 2. Empty/whitespace RETAILFLOW_CONNECTION falls back to Configuration
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", "   ");
            var withConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:RetailFlow"] = "Server=cfg-host;Database=RetailFlowProduct;"
                })
                .Build();
            Assert.Equal(
                "Server=cfg-host;Database=RetailFlowProduct;",
                ConnectionStringResolver.ResolveRuntime(withConfig, isDevelopment: false));

            // 3. Non-empty RETAILFLOW_CONNECTION overrides Configuration
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", "Server=env-host;Database=RetailFlowProduct;");
            Assert.Equal(
                "Server=env-host;Database=RetailFlowProduct;",
                ConnectionStringResolver.ResolveRuntime(withConfig, isDevelopment: false));

            // 4. Missing both in Development -> local fallback works
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", null);
            var emptyConfig = new ConfigurationBuilder().Build();
            var devConn = ConnectionStringResolver.ResolveRuntime(emptyConfig, isDevelopment: true);
            Assert.Equal(ConnectionStringResolver.DefaultLocalConnectionString, devConn);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RETAILFLOW_CONNECTION", originalEnv);
        }
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
            store.Id,
            currency,
            "UTC",
            deliveryEnabled: true,
            pickupEnabled: true);

        return (store, settings);
    }

    private static string NewSuffix() => Guid.NewGuid().ToString("N")[..8];
}

public sealed class CatalogSqlTestFixture : IAsyncLifetime
{
    private const string DefaultLocalConnectionString =
        "Server=.;Database=RetailFlowProduct;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

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
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["ConnectionStrings:RetailFlow"] = connectionStringOverride ?? ConnectionString
                    });
                });
            });
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
        await db.ProductImages.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.InventoryItems.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Products.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
        await db.Categories.Where(x => storeIds.Contains(x.StoreId)).ExecuteDeleteAsync();
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
