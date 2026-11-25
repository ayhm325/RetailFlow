using RetailFlow.Application.Stores;

namespace RetailFlow.Application.Catalog;

public interface IPublicCatalogQueryService
{
    Task<CatalogCategoriesResponse> GetCategoriesAsync(
        StoreRequestContext store,
        CancellationToken cancellationToken);

    Task<CatalogProductsResponse> GetProductsAsync(
        StoreRequestContext store,
        CatalogProductsQuery query,
        CancellationToken cancellationToken);

    Task<CatalogProductDetailsDto?> GetProductDetailsAsync(
        StoreRequestContext store,
        Guid productId,
        CancellationToken cancellationToken);
}
