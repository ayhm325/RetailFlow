using RetailFlow.Domain.Stores;

namespace RetailFlow.Application.Catalog;

public sealed record PublicStoreIdentityDto(
    string StoreName,
    string StoreSlug,
    StoreType StoreType,
    string CurrencyCode,
    string? LogoUrl,
    string? PrimaryColor,
    string? ContactEmail,
    string? ContactPhone,
    bool DeliveryEnabled,
    bool PickupEnabled,
    string? PublicAddress);

public sealed record PublicDeliveryZoneDto(
    Guid Id,
    string Name,
    decimal FlatFee);

public sealed record PublicDeliveryZonesResponse(
    string StoreName,
    string StoreSlug,
    string CurrencyCode,
    bool DeliveryEnabled,
    IReadOnlyList<PublicDeliveryZoneDto> Zones);

public enum CatalogSortOption
{
    Newest = 0,
    PriceAsc = 1,
    PriceDesc = 2,
    NameAsc = 3,
    NameDesc = 4
}

public sealed record CatalogProductsQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? CategoryId,
    string? CategorySlug,
    CatalogSortOption Sort)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public const int MaxSearchLength = 100;

    public static bool TryParseSort(
        string? rawSort,
        out CatalogSortOption sort)
    {
        if (string.IsNullOrWhiteSpace(rawSort))
        {
            sort = CatalogSortOption.Newest;
            return true;
        }

        switch (rawSort.Trim().ToLowerInvariant())
        {
            case "newest":
                sort = CatalogSortOption.Newest;
                return true;
            case "price_asc":
                sort = CatalogSortOption.PriceAsc;
                return true;
            case "price_desc":
                sort = CatalogSortOption.PriceDesc;
                return true;
            case "name_asc":
                sort = CatalogSortOption.NameAsc;
                return true;
            case "name_desc":
                sort = CatalogSortOption.NameDesc;
                return true;
            default:
                sort = default;
                return false;
        }
    }
}

public sealed record CatalogCategoryDto(
    Guid Id,
    string Name,
    string Slug,
    int SortOrder);

public sealed record CatalogCategoriesResponse(
    string StoreName,
    string StoreSlug,
    IReadOnlyList<CatalogCategoryDto> Categories);

public sealed record CatalogProductSummaryDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    string Name,
    decimal Price,
    string CurrencyCode,
    string? PrimaryImageUrl,
    bool IsAvailable);

public sealed record CatalogProductsResponse(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<CatalogProductSummaryDto> Items);

public sealed record CatalogProductImageDto(
    string ImageUrl,
    string? AltText,
    int SortOrder);

public sealed record CatalogProductDetailsDto(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    string CategorySlug,
    string Name,
    string? Description,
    string? Sku,
    decimal Price,
    string CurrencyCode,
    bool IsAvailable,
    IReadOnlyList<CatalogProductImageDto> Images);
