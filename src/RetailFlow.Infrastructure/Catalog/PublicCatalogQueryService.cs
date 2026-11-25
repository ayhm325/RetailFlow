using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Catalog;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Catalog;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Catalog;

public sealed class PublicCatalogQueryService : IPublicCatalogQueryService
{
    private readonly RetailFlowDbContext _dbContext;

    public PublicCatalogQueryService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatalogCategoriesResponse> GetCategoriesAsync(
        StoreRequestContext store,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        var categories = await _dbContext.Categories
            .AsNoTracking()
            .Where(c => c.StoreId == store.StoreId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ThenBy(c => c.Id)
            .Select(c => new CatalogCategoryDto(
                c.Id,
                c.Name,
                c.Slug,
                c.SortOrder))
            .ToListAsync(cancellationToken);

        return new CatalogCategoriesResponse(
            store.StoreName,
            store.StoreSlug,
            categories);
    }

    public async Task<CatalogProductsResponse> GetProductsAsync(
        StoreRequestContext store,
        CatalogProductsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(query);

        var productsQuery = _dbContext.Products
            .AsNoTracking()
            .Where(p =>
                p.StoreId == store.StoreId &&
                p.Status == ProductStatus.Published);

        if (query.CategoryId.HasValue)
        {
            var categoryId = query.CategoryId.Value;
            productsQuery = productsQuery.Where(p =>
                p.CategoryId == categoryId &&
                p.Category != null &&
                p.Category.StoreId == store.StoreId &&
                p.Category.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(query.CategorySlug))
        {
            var normalizedCategorySlug = query.CategorySlug.Trim().ToLowerInvariant();
            productsQuery = productsQuery.Where(p =>
                p.Category != null &&
                p.Category.StoreId == store.StoreId &&
                p.Category.IsActive &&
                p.Category.Slug == normalizedCategorySlug);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            productsQuery = productsQuery.Where(p =>
                p.Name.Contains(search) ||
                (p.Description != null && p.Description.Contains(search)) ||
                (p.Sku != null && p.Sku.Contains(search)));
        }

        var totalCount = await productsQuery.CountAsync(cancellationToken);

        var orderedQuery = query.Sort switch
        {
            CatalogSortOption.PriceAsc => productsQuery
                .OrderBy(p => p.Price)
                .ThenBy(p => p.Id),

            CatalogSortOption.PriceDesc => productsQuery
                .OrderByDescending(p => p.Price)
                .ThenBy(p => p.Id),

            CatalogSortOption.NameAsc => productsQuery
                .OrderBy(p => p.Name)
                .ThenBy(p => p.Id),

            CatalogSortOption.NameDesc => productsQuery
                .OrderByDescending(p => p.Name)
                .ThenBy(p => p.Id),

            _ => productsQuery
                .OrderByDescending(p => p.CreatedAtUtc)
                .ThenBy(p => p.Id)
        };

        var skipLong = (long)(query.Page - 1) * query.PageSize;

        IReadOnlyList<CatalogProductSummaryDto> items;
        if (totalCount == 0 || skipLong >= totalCount)
        {
            items = [];
        }
        else
        {
            items = await orderedQuery
                .Skip((int)skipLong)
                .Take(query.PageSize)
                .Select(p => new CatalogProductSummaryDto(
                    p.Id,
                    p.CategoryId,
                    p.Category!.Name,
                    p.Category.Slug,
                    p.Name,
                    p.Price,
                    store.CurrencyCode,
                    p.Images
                        .OrderBy(i => i.SortOrder)
                        .ThenBy(i => i.Id)
                        .Select(i => i.ImageUrl)
                        .FirstOrDefault(),
                    p.InventoryItem != null &&
                    (p.InventoryItem.OnHand - p.InventoryItem.Reserved) > 0))
                .ToListAsync(cancellationToken);
        }

        var totalPages = totalCount == 0
            ? 0
            : (int)Math.Ceiling(totalCount / (double)query.PageSize);

        return new CatalogProductsResponse(
            query.Page,
            query.PageSize,
            totalCount,
            totalPages,
            items);
    }

    public async Task<CatalogProductDetailsDto?> GetProductDetailsAsync(
        StoreRequestContext store,
        Guid productId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (productId == Guid.Empty)
        {
            return null;
        }

        return await _dbContext.Products
            .AsNoTracking()
            .Where(p =>
                p.StoreId == store.StoreId &&
                p.Id == productId &&
                p.Status == ProductStatus.Published)
            .Select(p => new CatalogProductDetailsDto(
                p.Id,
                p.CategoryId,
                p.Category!.Name,
                p.Category.Slug,
                p.Name,
                p.Description,
                p.Sku,
                p.Price,
                store.CurrencyCode,
                p.InventoryItem != null &&
                (p.InventoryItem.OnHand - p.InventoryItem.Reserved) > 0,
                p.Images
                    .OrderBy(i => i.SortOrder)
                    .ThenBy(i => i.Id)
                    .Select(i => new CatalogProductImageDto(
                        i.ImageUrl,
                        i.AltText,
                        i.SortOrder))
                    .ToList()))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
