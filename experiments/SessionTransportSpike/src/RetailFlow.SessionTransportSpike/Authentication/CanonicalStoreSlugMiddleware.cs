using Microsoft.AspNetCore.Http;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.SessionTransportSpike.Authentication;

public sealed class CanonicalStoreSlugMiddleware
{
    private const string StoreRoutePrefix = "/api/stores/";
    private const string CustomersSegment = "customers";

    private readonly RequestDelegate _next;

    public CanonicalStoreSlugMiddleware(RequestDelegate next)
    {
        _next = next
            ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var metadata = context.GetEndpoint()?
            .Metadata
            .GetMetadata<IdentityScopeMetadata>();

        if (metadata?.AccountKind != AccountKind.StoreCustomer)
        {
            await _next(context);
            return;
        }

        if (!context.Request.RouteValues.TryGetValue(
                "slug",
                out var slugValue) ||
            slugValue is not string slug ||
            string.IsNullOrWhiteSpace(slug))
        {
            await _next(context);
            return;
        }

        if (!IsCanonicalStorePath(context.Request.Path, slug))
        {
            await Results.Problem(
                title: "Non-canonical store URL.",
                detail:
                    "Store URLs must use the lowercase canonical route.",
                statusCode: StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);

            return;
        }

        await _next(context);
    }

    private static bool IsCanonicalStorePath(
        PathString requestPath,
        string slug)
    {
        var escapedSlug =
            Uri.EscapeDataString(slug.ToLowerInvariant());

        var canonicalPrefix =
            $"{StoreRoutePrefix}{escapedSlug}/{CustomersSegment}";

        var path = requestPath.Value;

        if (path is null ||
            !path.StartsWith(
                canonicalPrefix,
                StringComparison.Ordinal))
        {
            return false;
        }

        // Prevent accepting paths such as:
        // /api/stores/store-a/customers-extra
        return path.Length == canonicalPrefix.Length ||
               path[canonicalPrefix.Length] == '/';
    }
}
