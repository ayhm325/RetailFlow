using Microsoft.AspNetCore.Mvc;

using RetailFlow.Application.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.Auth;

/// <summary>
/// Resolves the tenant IdentityLookupScope AFTER routing and BEFORE UseAuthentication()
/// so CookieAuthentication / SecurityStampValidator / ScopedUserStore always run within a verified tenant scope.
/// Fails closed if a store slug does not resolve to an active store.
/// </summary>
public sealed class RetailFlowIdentityScopeMiddleware
{
    private readonly RequestDelegate _next;

    public RetailFlowIdentityScopeMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(
        HttpContext context,
        IStoreDirectoryResolver storeDirectoryResolver,
        IStoreRequestContextAccessor storeContextAccessor,
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        var metadata = context.GetEndpoint()?.Metadata.GetMetadata<IdentityScopeMetadata>();

        if (metadata?.ScopeType == IdentityScopeType.Platform ||
            (metadata is null && context.Request.Path.StartsWithSegments("/api/platform", StringComparison.OrdinalIgnoreCase)))
        {
            scopeAccessor.SetCurrent(IdentityLookupScope.ForPlatform());
            await _next(context);
            return;
        }

        var isStoreScoped = metadata?.ScopeType == IdentityScopeType.Store ||
            (metadata is null && context.Request.RouteValues.ContainsKey("slug"));

        if (isStoreScoped)
        {
            var slug = context.GetRouteValue("slug") as string;
            if (string.IsNullOrWhiteSpace(slug))
            {
                await WriteStoreNotFoundAsync(context);
                return;
            }

            var store = await storeDirectoryResolver.ResolveActiveStoreAsync(
                slug,
                context.RequestAborted);

            if (store is null)
            {
                await WriteStoreNotFoundAsync(context);
                return;
            }

            storeContextAccessor.SetCurrent(store);
            scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(store.StoreId));
        }

        await _next(context);
    }

    private static async Task WriteStoreNotFoundAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        var problemDetailsService = context.RequestServices.GetService<IProblemDetailsService>();
        if (problemDetailsService is not null)
        {
            await problemDetailsService.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Store not found",
                    Detail = "The requested store was not found or is not publicly available."
                }
            });
        }
    }
}
