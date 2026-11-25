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
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PublicStoreCatalogIntegrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CatalogSqlTestFixture _fixture;

    public PublicStoreCatalogIntegrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ActiveStore_ReturnsCategoriesAndProducts()
    {
        var suffix = NewSuffix();
        var store = Store.Create(
            Guid.NewGuid(),
            $"Active Store {suffix}",
            $"active-{suffix}",
            StoreType.OnlineOnly,
            StoreStatus.Active,
            "usd",
            DateTimeOffset.UtcNow);

        var category = Category.Create(
            Guid.NewGuid(),
            store.Id,
            "Electronics",
            $"electronics-{suffix}",
            isActive: true,
            sortOrder: 1);

        var product = Product.Create(
            Guid.NewGuid(),
            store.Id,
            category.Id,
            "Wireless Mouse",
            29.99m,
            ProductStatus.Published,
            DateTimeOffset.UtcNow,
            description: "Ergonomic wireless mouse",
            sku: $"SKU-{suffix}-1");

        var image = ProductImage.Create(
            Guid.NewGuid(),
            store.Id,
            product.Id,
            "https://cdn.example.com/mouse.png",
            sortOrder: 0,
            altText: "Wireless Mouse");

        var inventory = InventoryItem.Create(
            Guid.NewGuid(),
            store.Id,
            product.Id,
            onHand: 12,
            reserved: 2);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
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
        var store = Store.Create(
            Guid.NewGuid(),
            $"Inactive Store {suffix}",
            $"inactive-{suffix}",
            StoreType.Physical,
            status,
            "SAR",
            DateTimeOffset.UtcNow);

        var category = Category.Create(Guid.NewGuid(), store.Id, "Cat", $"cat-{suffix}", true, 1);
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Prod", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
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
        var storeA = Store.Create(Guid.NewGuid(), "Store A", $"store-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var storeB = Store.Create(Guid.NewGuid(), "Store B", $"store-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);

        var catA2 = Category.Create(Guid.NewGuid(), storeA.Id, "Zeta", $"zeta-{suffix}", true, 2);
        var catA1b = Category.Create(Guid.NewGuid(), storeA.Id, "Beta", $"beta-{suffix}", true, 1);
        var catA1a = Category.Create(Guid.NewGuid(), storeA.Id, "Alpha", $"alpha-{suffix}", true, 1);
        var catAInactive = Category.Create(Guid.NewGuid(), storeA.Id, "Hidden", $"hidden-{suffix}", false, 0);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "OtherStore", $"other-{suffix}", true, 0);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
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
    public async Task Products_ExcludeDraftArchivedAndOtherStoreProducts()
    {
        var suffix = NewSuffix();
        var storeA = Store.Create(Guid.NewGuid(), "Store A", $"vis-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var storeB = Store.Create(Guid.NewGuid(), "Store B", $"vis-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);

        var catA = Category.Create(Guid.NewGuid(), storeA.Id, "General", $"gen-a-{suffix}", true, 1);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "General", $"gen-b-{suffix}", true, 1);

        var publishedA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Published A", 15m, ProductStatus.Published, DateTimeOffset.UtcNow);
        var draftA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Draft A", 15m, ProductStatus.Draft, DateTimeOffset.UtcNow);
        var archivedA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Archived A", 15m, ProductStatus.Archived, DateTimeOffset.UtcNow);
        var publishedB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Published B", 15m, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.Categories.AddRange(catA, catB);
            db.Products.AddRange(publishedA, draftA, archivedA, publishedB);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            using var listResp = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/products");
            Assert.Equal(HttpStatusCode.OK, listResp.StatusCode);
            var listPayload = await listResp.Content.ReadFromJsonAsync<CatalogProductsResponse>(JsonOptions);
            Assert.NotNull(listPayload);
            var single = Assert.Single(listPayload.Items);
            Assert.Equal(publishedA.Id, single.Id);

            foreach (var hiddenId in new[] { draftA.Id, archivedA.Id, publishedB.Id })
            {
                using var detailResp = await client.GetAsync($"/api/stores/{storeA.Slug}/catalog/products/{hiddenId}");
                Assert.Equal(HttpStatusCode.NotFound, detailResp.StatusCode);
            }
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
        var store = Store.Create(Guid.NewGuid(), "Catalog Store", $"cat-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "EUR", DateTimeOffset.UtcNow);
        var catBooks = Category.Create(Guid.NewGuid(), store.Id, "Books", $"books-{suffix}", true, 1);
        var catGear = Category.Create(Guid.NewGuid(), store.Id, "Gear", $"gear-{suffix}", true, 2);
        var catHidden = Category.Create(Guid.NewGuid(), store.Id, "Hidden", $"hidden-{suffix}", false, 3);

        var t0 = new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var p1 = Product.Create(Guid.NewGuid(), store.Id, catBooks.Id, "Alpha Book", 30m, ProductStatus.Published, t0, sku: $"BK-{suffix}-01");
        var p2 = Product.Create(Guid.NewGuid(), store.Id, catBooks.Id, "Gamma Guide", 10m, ProductStatus.Published, t0.AddMinutes(1), sku: $"BK-{suffix}-02");
        var p3 = Product.Create(Guid.NewGuid(), store.Id, catGear.Id, "Beta Lamp", 20m, ProductStatus.Published, t0.AddMinutes(2), sku: $"GR-{suffix}-03");
        var pHiddenCat = Product.Create(Guid.NewGuid(), store.Id, catHidden.Id, "Hidden Cat Item", 5m, ProductStatus.Published, t0.AddMinutes(3), sku: $"HD-{suffix}-04");

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.Categories.AddRange(catBooks, catGear, catHidden);
            db.Products.AddRange(p1, p2, p3, pHiddenCat);
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

            // Filter by inactive categorySlug returns empty
            var hiddenCatResult = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categorySlug={catHidden.Slug}",
                JsonOptions);
            Assert.NotNull(hiddenCatResult);
            Assert.Empty(hiddenCatResult.Items);

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
            Assert.Equal([p2.Id, pHiddenCat.Id], page2.Items.Select(i => i.Id).ToArray());

            // Out-of-range page returns empty items
            var page3 = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?sort=name_asc&page=3&pageSize=2",
                JsonOptions);
            Assert.NotNull(page3);
            Assert.Empty(page3.Items);
            Assert.Equal(4, page3.TotalCount);

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
        var store = Store.Create(Guid.NewGuid(), "Inv Store", $"inv-{suffix}", StoreType.Physical, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var category = Category.Create(Guid.NewGuid(), store.Id, "Cat", $"inv-cat-{suffix}", true, 1);

        var availableProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Available", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);
        var reservedOutProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "ReservedOut", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);
        var noInventoryProd = Product.Create(Guid.NewGuid(), store.Id, category.Id, "NoInventory", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);

        var availableInventory = InventoryItem.Create(Guid.NewGuid(), store.Id, availableProd.Id, onHand: 10, reserved: 3);
        var reservedOutInventory = InventoryItem.Create(Guid.NewGuid(), store.Id, reservedOutProd.Id, onHand: 5, reserved: 5);

        Assert.Equal(7, availableInventory.Available);
        Assert.Equal(0, reservedOutInventory.Available);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
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
    public async Task DatabaseConstraints_EnforceTenantIsolationAndDomainInvariants()
    {
        var suffix = NewSuffix();
        var storeA = Store.Create(Guid.NewGuid(), "Store A", $"const-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var storeB = Store.Create(Guid.NewGuid(), "Store B", $"const-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var catA = Category.Create(Guid.NewGuid(), storeA.Id, "Cat A", $"cat-a-{suffix}", true, 1);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "Cat B", $"cat-b-{suffix}", true, 1);
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Prod A", 10m, ProductStatus.Published, DateTimeOffset.UtcNow, sku: $"SHARED-SKU-{suffix}");

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.Categories.AddRange(catA, catB);
            db.Products.Add(prodA);
            await db.SaveChangesAsync();
        }

        try
        {
            // 1. Duplicate Store.Slug rejected
            await using (var db = _fixture.CreateDbContext())
            {
                db.Stores.Add(Store.Create(Guid.NewGuid(), "Dup", storeA.Slug, StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // 2. Duplicate (StoreId, Sku) rejected in same store
            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Dup Sku", 12m, ProductStatus.Published, DateTimeOffset.UtcNow, sku: prodA.Sku));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // 3. Same Sku allowed in different store, and multiple NULL Skus allowed in same store
            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Store B Same Sku", 12m, ProductStatus.Published, DateTimeOffset.UtcNow, sku: prodA.Sku));
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Null Sku 1", 12m, ProductStatus.Published, DateTimeOffset.UtcNow));
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Null Sku 2", 12m, ProductStatus.Published, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync();
            }

            // 4. Cross-store Product -> Category FK rejected
            await using (var db = _fixture.CreateDbContext())
            {
                db.Products.Add(Product.Create(Guid.NewGuid(), storeA.Id, catB.Id, "Cross Cat", 10m, ProductStatus.Published, DateTimeOffset.UtcNow));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // 5. Cross-store InventoryItem -> Product FK rejected
            await using (var db = _fixture.CreateDbContext())
            {
                db.InventoryItems.Add(InventoryItem.Create(Guid.NewGuid(), storeB.Id, prodA.Id, 5, 0));
                await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            }

            // 6. Check constraints: negative OnHand, negative Reserved, Reserved > OnHand
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
        var store = Store.Create(Guid.NewGuid(), "Val Store", $"val-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
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
        var store = Store.Create(Guid.NewGuid(), "Val Store", $"val-len-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
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
    public async Task Products_LargePageValue_DoesNotOverflowAndReturnsEmptyItems()
    {
        var suffix = NewSuffix();
        var store = Store.Create(Guid.NewGuid(), "Page Store", $"pg-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var category = Category.Create(Guid.NewGuid(), store.Id, "Cat", $"pg-cat-{suffix}", true, 1);
        var product = Product.Create(Guid.NewGuid(), store.Id, category.Id, "Item", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.Categories.Add(category);
            db.Products.Add(product);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();
            var response = await client.GetAsync($"/api/stores/{store.Slug}/catalog/products?page={int.MaxValue}&pageSize=50");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content.ReadFromJsonAsync<CatalogProductsResponse>(JsonOptions);
            Assert.NotNull(payload);
            Assert.Equal(int.MaxValue, payload.Page);
            Assert.Equal(50, payload.PageSize);
            Assert.Equal(1, payload.TotalCount);
            Assert.Equal(1, payload.TotalPages);
            Assert.Empty(payload.Items);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task Products_CategoryIdAndCategorySlugTogether_AndInactiveCategoryId_BehaveDeterministically()
    {
        var suffix = NewSuffix();
        var store = Store.Create(Guid.NewGuid(), "Cat Filter Store", $"cf-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var catActive = Category.Create(Guid.NewGuid(), store.Id, "Active", $"active-cat-{suffix}", true, 1);
        var catOther = Category.Create(Guid.NewGuid(), store.Id, "Other", $"other-cat-{suffix}", true, 2);
        var catInactive = Category.Create(Guid.NewGuid(), store.Id, "Inactive", $"inactive-cat-{suffix}", false, 3);

        var prodActive = Product.Create(Guid.NewGuid(), store.Id, catActive.Id, "Active Item", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);
        var prodInactive = Product.Create(Guid.NewGuid(), store.Id, catInactive.Id, "Inactive Cat Item", 10m, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.Categories.AddRange(catActive, catOther, catInactive);
            db.Products.AddRange(prodActive, prodInactive);
            await db.SaveChangesAsync();
        }

        try
        {
            using var client = _fixture.CreateHttpsClient();

            var matching = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catActive.Id}&categorySlug={catActive.Slug}",
                JsonOptions);
            Assert.NotNull(matching);
            Assert.Equal([prodActive.Id], matching.Items.Select(i => i.Id).ToArray());

            var conflicting = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catActive.Id}&categorySlug={catOther.Slug}",
                JsonOptions);
            Assert.NotNull(conflicting);
            Assert.Empty(conflicting.Items);

            var inactiveById = await client.GetFromJsonAsync<CatalogProductsResponse>(
                $"/api/stores/{store.Slug}/catalog/products?categoryId={catInactive.Id}",
                JsonOptions);
            Assert.NotNull(inactiveById);
            Assert.Empty(inactiveById.Items);
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
        var storeA = Store.Create(Guid.NewGuid(), "Store A", $"conc-a-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "USD", DateTimeOffset.UtcNow);
        var storeB = Store.Create(Guid.NewGuid(), "Store B", $"conc-b-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, "KWD", DateTimeOffset.UtcNow);
        var catA = Category.Create(Guid.NewGuid(), storeA.Id, "Cat A", $"conc-cat-a-{suffix}", true, 1);
        var catB = Category.Create(Guid.NewGuid(), storeB.Id, "Cat B", $"conc-cat-b-{suffix}", true, 1);
        var prodA = Product.Create(Guid.NewGuid(), storeA.Id, catA.Id, "Product A", 12.50m, ProductStatus.Published, DateTimeOffset.UtcNow);
        var prodB = Product.Create(Guid.NewGuid(), storeB.Id, catB.Id, "Product B", 4.250m, ProductStatus.Published, DateTimeOffset.UtcNow);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
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

    public WebApplicationFactory<Program> CreateProductionFactory()
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
                        ["ConnectionStrings:RetailFlow"] = ConnectionString
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
