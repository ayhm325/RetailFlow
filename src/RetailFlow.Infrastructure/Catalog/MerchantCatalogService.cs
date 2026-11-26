using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Catalog;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Inventory;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Catalog;

public sealed class MerchantCatalogService : IMerchantCatalogService
{
    private readonly RetailFlowDbContext _dbContext;

    public MerchantCatalogService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MerchantCategoryDto> CreateCategoryAsync(
        Guid storeId,
        CreateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var category = Category.Create(
            Guid.NewGuid(),
            storeId,
            command.Name,
            command.Slug,
            command.IsActive,
            command.SortOrder);

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(category);
    }

    public async Task<MerchantCategoryDto?> UpdateCategoryAsync(
        Guid storeId,
        Guid categoryId,
        UpdateCategoryCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var category = await _dbContext.Categories
            .SingleOrDefaultAsync(c => c.Id == categoryId && c.StoreId == storeId, cancellationToken);

        if (category is null)
        {
            return null;
        }

        category.Update(command.Name, command.Slug, command.IsActive, command.SortOrder);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(category);
    }

    public async Task<MerchantProductDto> CreateProductAsync(
        Guid storeId,
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = await _dbContext.StoreSettings
            .Include(ss => ss.Currency)
            .SingleOrDefaultAsync(ss => ss.StoreId == storeId, cancellationToken)
            ?? throw new InvalidOperationException($"StoreSettings not found for store '{storeId}'.");

        var currency = settings.Currency
            ?? throw new InvalidOperationException($"Currency '{settings.CurrencyCode}' not found.");

        var categoryExistsInStore = await _dbContext.Categories
            .AnyAsync(c => c.Id == command.CategoryId && c.StoreId == storeId, cancellationToken);

        if (!categoryExistsInStore)
        {
            throw new InvalidOperationException($"Category '{command.CategoryId}' does not belong to store '{storeId}'.");
        }

        var product = Product.Create(
            Guid.NewGuid(),
            storeId,
            command.CategoryId,
            command.Name,
            command.Price,
            settings,
            currency,
            command.InitialStatus,
            DateTimeOffset.UtcNow,
            command.Description,
            command.Sku);

        var inventory = InventoryItem.Create(
            Guid.NewGuid(),
            storeId,
            product.Id,
            command.InitialOnHand,
            reserved: 0);

        _dbContext.Products.Add(product);
        _dbContext.InventoryItems.Add(inventory);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(product, settings.CurrencyCode, inventory);
    }

    public async Task<MerchantProductDto?> UpdateProductAsync(
        Guid storeId,
        Guid productId,
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await _dbContext.Products
            .Include(p => p.InventoryItem)
            .SingleOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        var settings = await _dbContext.StoreSettings
            .Include(ss => ss.Currency)
            .SingleOrDefaultAsync(ss => ss.StoreId == storeId, cancellationToken)
            ?? throw new InvalidOperationException($"StoreSettings not found for store '{storeId}'.");

        var currency = settings.Currency
            ?? throw new InvalidOperationException($"Currency '{settings.CurrencyCode}' not found.");

        var categoryExistsInStore = await _dbContext.Categories
            .AnyAsync(c => c.Id == command.CategoryId && c.StoreId == storeId, cancellationToken);

        if (!categoryExistsInStore)
        {
            throw new InvalidOperationException($"Category '{command.CategoryId}' does not belong to store '{storeId}'.");
        }

        product.UpdateDetails(
            command.CategoryId,
            command.Name,
            command.Price,
            settings,
            currency,
            command.Description,
            command.Sku);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(product, settings.CurrencyCode, product.InventoryItem);
    }

    public async Task<bool> PublishProductAsync(
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .SingleOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, cancellationToken);

        if (product is null)
        {
            return false;
        }

        product.Publish();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ArchiveProductAsync(
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        var product = await _dbContext.Products
            .SingleOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, cancellationToken);

        if (product is null)
        {
            return false;
        }

        product.Archive();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SetInventoryOnHandAsync(
        Guid storeId,
        Guid productId,
        int onHand,
        CancellationToken cancellationToken)
    {
        var inventory = await _dbContext.InventoryItems
            .SingleOrDefaultAsync(i => i.ProductId == productId && i.StoreId == storeId, cancellationToken);

        if (inventory is null)
        {
            return false;
        }

        inventory.SetOnHand(onHand);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MerchantProductImageDto?> AddProductImageAsync(
        Guid storeId,
        Guid productId,
        AddProductImageCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await _dbContext.Products
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.Id == productId && p.StoreId == storeId, cancellationToken);

        if (product is null)
        {
            return null;
        }

        if (product.Status == ProductStatus.Archived)
        {
            throw new InvalidOperationException("Cannot add images to an archived product.");
        }

        var image = ProductImage.Create(
            Guid.NewGuid(),
            storeId,
            productId,
            command.ImageUrl,
            command.SortOrder,
            command.AltText);

        _dbContext.ProductImages.Add(image);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new MerchantProductImageDto(
            image.Id,
            image.StoreId,
            image.ProductId,
            image.ImageUrl,
            image.AltText,
            image.SortOrder);
    }

    public async Task<bool> RemoveProductImageAsync(
        Guid storeId,
        Guid productId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        var image = await _dbContext.ProductImages
            .SingleOrDefaultAsync(
                i => i.Id == imageId && i.ProductId == productId && i.StoreId == storeId,
                cancellationToken);

        if (image is null)
        {
            return false;
        }

        _dbContext.ProductImages.Remove(image);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static MerchantCategoryDto Map(Category category) =>
        new(category.Id, category.StoreId, category.Name, category.Slug, category.SortOrder, category.IsActive);

    private static MerchantProductDto Map(Product product, string currencyCode, InventoryItem? inventory) =>
        new(
            product.Id,
            product.StoreId,
            product.CategoryId,
            product.Name,
            product.Description,
            product.Sku,
            product.Price,
            currencyCode,
            product.Status,
            inventory?.OnHand ?? 0,
            inventory?.Reserved ?? 0);
}
