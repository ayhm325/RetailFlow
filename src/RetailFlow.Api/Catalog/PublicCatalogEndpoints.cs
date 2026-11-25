using RetailFlow.Application.Catalog;
using RetailFlow.Application.Stores;

namespace RetailFlow.Api.Catalog;

/// <summary>
/// Public, unauthenticated read-only storefront catalog endpoints.
/// </summary>
public static class PublicCatalogEndpoints
{
    public static WebApplication MapPublicCatalogEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/stores/{slug}/catalog")
            .AddEndpointFilter<StoreResolutionEndpointFilter>();

        group.MapGet("/categories", GetCategoriesAsync)
            .WithName("GetPublicStoreCategories");

        group.MapGet("/products", GetProductsAsync)
            .WithName("GetPublicStoreProducts");

        group.MapGet("/products/{productId:guid}", GetProductDetailsAsync)
            .WithName("GetPublicStoreProductDetails");

        return app;
    }

    private static async Task<IResult> GetCategoriesAsync(
        IStoreRequestContextAccessor storeContextAccessor,
        IPublicCatalogQueryService catalogQueryService,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var response = await catalogQueryService.GetCategoriesAsync(store, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> GetProductsAsync(
        int? page,
        int? pageSize,
        string? search,
        Guid? categoryId,
        string? categorySlug,
        string? sort,
        IStoreRequestContextAccessor storeContextAccessor,
        IPublicCatalogQueryService catalogQueryService,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        var resolvedPage = page ?? CatalogProductsQuery.DefaultPage;
        if (resolvedPage < 1)
        {
            errors["page"] = ["Page must be greater than or equal to 1."];
        }

        var resolvedPageSize = pageSize ?? CatalogProductsQuery.DefaultPageSize;
        if (resolvedPageSize < 1 || resolvedPageSize > CatalogProductsQuery.MaxPageSize)
        {
            errors["pageSize"] = [$"PageSize must be between 1 and {CatalogProductsQuery.MaxPageSize}."];
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (normalizedSearch is not null && normalizedSearch.Length > CatalogProductsQuery.MaxSearchLength)
        {
            errors["search"] = [$"Search must not exceed {CatalogProductsQuery.MaxSearchLength} characters."];
        }

        if (!CatalogProductsQuery.TryParseSort(sort, out var resolvedSort))
        {
            errors["sort"] = ["Sort must be one of: newest, price_asc, price_desc, name_asc, name_desc."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var normalizedCategorySlug = string.IsNullOrWhiteSpace(categorySlug)
            ? null
            : categorySlug.Trim();

        var query = new CatalogProductsQuery(
            Page: resolvedPage,
            PageSize: resolvedPageSize,
            Search: normalizedSearch,
            CategoryId: categoryId,
            CategorySlug: normalizedCategorySlug,
            Sort: resolvedSort);

        var response = await catalogQueryService.GetProductsAsync(store, query, cancellationToken);
        return TypedResults.Ok(response);
    }

    private static async Task<IResult> GetProductDetailsAsync(
        Guid productId,
        IStoreRequestContextAccessor storeContextAccessor,
        IPublicCatalogQueryService catalogQueryService,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var product = await catalogQueryService.GetProductDetailsAsync(
            store,
            productId,
            cancellationToken);

        if (product is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Product not found",
                detail: "The requested product was not found in this store.");
        }

        return TypedResults.Ok(product);
    }
}
