using RetailFlow.Application.Stores;

namespace RetailFlow.Api.Catalog;

/// <summary>
/// Resolves an active, publicly accessible store from the {slug} route parameter
/// and populates <see cref="IStoreRequestContextAccessor"/> for the current request.
/// </summary>
public sealed class StoreResolutionEndpointFilter(
    IStoreDirectoryResolver storeDirectoryResolver,
    IStoreRequestContextAccessor storeRequestContextAccessor) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var slug = context.HttpContext.GetRouteValue("slug") as string;
        if (string.IsNullOrWhiteSpace(slug))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Store not found",
                detail: "The requested store was not found or is not publicly available.");
        }

        var store = await storeDirectoryResolver.ResolveActiveStoreAsync(
            slug,
            context.HttpContext.RequestAborted);

        if (store is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Store not found",
                detail: "The requested store was not found or is not publicly available.");
        }

        storeRequestContextAccessor.SetCurrent(store);
        return await next(context);
    }
}
