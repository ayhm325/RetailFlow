using Microsoft.AspNetCore.Http;
using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

public sealed class IdentityScopeMiddleware
{
    private readonly RequestDelegate _next;

    public IdentityScopeMiddleware(RequestDelegate next)
    {
        _next = next
            ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(
        HttpContext context,
        IIdentityScopeResolver resolver,
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        var metadata = context.GetEndpoint()?
            .Metadata
            .GetMetadata<IdentityScopeMetadata>();

        // Endpoints without identity scope metadata
        // continue through the pipeline unchanged.
        if (metadata is null)
        {
            await _next(context);
            return;
        }

        IdentityLookupScope scope;

        switch (metadata.AccountKind)
        {
            case AccountKind.StoreCustomer:
            {
                if (!context.Request.RouteValues.TryGetValue(
                        "slug",
                        out var slugValue) ||
                    slugValue is not string slug ||
                    string.IsNullOrWhiteSpace(slug))
                {
                    context.Response.StatusCode =
                        StatusCodes.Status404NotFound;

                    return;
                }

                var storeId = await resolver.ResolveActiveStoreIdAsync(
                    slug,
                    context.RequestAborted);

                // Unknown and inactive stores have
                // the same external response.
                if (storeId is null)
                {
                    context.Response.StatusCode =
                        StatusCodes.Status404NotFound;

                    return;
                }

                scope = IdentityLookupScope.ForStoreCustomer(
                    storeId.Value);

                break;
            }

            case AccountKind.BackOffice:
            {
                scope = IdentityLookupScope.ForBackOffice();
                break;
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported identity account kind: " +
                    $"{metadata.AccountKind}.");
        }

        // Establish the request identity scope exactly once.
        scopeAccessor.SetCurrent(scope);

        await _next(context);
    }
}
