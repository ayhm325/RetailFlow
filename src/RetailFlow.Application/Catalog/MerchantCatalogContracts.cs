using RetailFlow.Domain.Catalog;

namespace RetailFlow.Application.Catalog;

public sealed record CreateCategoryCommand(
    string Name,
    string Slug,
    int SortOrder = 0,
    bool IsActive = true);

public sealed record UpdateCategoryCommand(
    string Name,
    string Slug,
    int SortOrder,
    bool IsActive);

public sealed record MerchantCategoryDto(
    Guid Id,
    Guid StoreId,
    string Name,
    string Slug,
    int SortOrder,
    bool IsActive);

public sealed record CreateProductCommand(
    Guid CategoryId,
    string Name,
    string? Description,
    string? Sku,
    decimal Price,
    ProductStatus InitialStatus = ProductStatus.Draft,
    int InitialOnHand = 0);

public sealed record UpdateProductCommand(
    Guid CategoryId,
    string Name,
    string? Description,
    string? Sku,
    decimal Price);

public sealed record MerchantProductDto(
    Guid Id,
    Guid StoreId,
    Guid CategoryId,
    string Name,
    string? Description,
    string? Sku,
    decimal Price,
    string CurrencyCode,
    ProductStatus Status,
    int OnHand,
    int Reserved);

public interface IMerchantCatalogService
{
    Task<MerchantCategoryDto> CreateCategoryAsync(
        Guid storeId,
        CreateCategoryCommand command,
        CancellationToken cancellationToken);

    Task<MerchantCategoryDto?> UpdateCategoryAsync(
        Guid storeId,
        Guid categoryId,
        UpdateCategoryCommand command,
        CancellationToken cancellationToken);

    Task<MerchantProductDto> CreateProductAsync(
        Guid storeId,
        CreateProductCommand command,
        CancellationToken cancellationToken);

    Task<MerchantProductDto?> UpdateProductAsync(
        Guid storeId,
        Guid productId,
        UpdateProductCommand command,
        CancellationToken cancellationToken);

    Task<bool> PublishProductAsync(
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken);

    Task<bool> ArchiveProductAsync(
        Guid storeId,
        Guid productId,
        CancellationToken cancellationToken);

    Task<bool> SetInventoryOnHandAsync(
        Guid storeId,
        Guid productId,
        int onHand,
        CancellationToken cancellationToken);
}
