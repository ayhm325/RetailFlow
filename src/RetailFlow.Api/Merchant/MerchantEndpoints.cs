using System.Security.Claims;

using RetailFlow.Api.Catalog;
using RetailFlow.Application.Catalog;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Orders;

namespace RetailFlow.Api.Merchant;

public static class MerchantEndpoints
{
    public sealed record SetInventoryRequest(int OnHand);
    public sealed record RejectOrderRequest(string? Reason);
    public sealed record AdvanceFulfillmentRequest(FulfillmentStatus NextStatus);

    public static WebApplication MapMerchantEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/stores/{slug}/merchant")
            .RequireAuthorization("MerchantOwner")
            .AddEndpointFilter<StoreResolutionEndpointFilter>()
            .AddEndpointFilter<TenantClaimMatchEndpointFilter>();

        group.MapPut("/branding", async Task<IResult> (
            UpdateStoreBrandingCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantStoreService storeService,
            CancellationToken ct) =>
        {
            var ok = await storeService.UpdateBrandingAsync(accessor.Current!.StoreId, command, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPut("/addresses", async Task<IResult> (
            UpdateStoreAddressesCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantStoreService storeService,
            CancellationToken ct) =>
        {
            var ok = await storeService.UpdateAddressesAsync(accessor.Current!.StoreId, command, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPost("/delivery-zones", async Task<IResult> (
            CreateDeliveryZoneCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantStoreService storeService,
            CancellationToken ct) =>
        {
            var zone = await storeService.CreateDeliveryZoneAsync(accessor.Current!.StoreId, command, ct);
            return TypedResults.Created($"/api/stores/{accessor.Current.StoreSlug}/delivery-zones", zone);
        });

        group.MapPut("/delivery-zones/{zoneId:guid}", async Task<IResult> (
            Guid zoneId,
            UpdateDeliveryZoneCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantStoreService storeService,
            CancellationToken ct) =>
        {
            var zone = await storeService.UpdateDeliveryZoneAsync(accessor.Current!.StoreId, zoneId, command, ct);
            return zone is null ? TypedResults.NotFound() : TypedResults.Ok(zone);
        });

        group.MapPost("/categories", async Task<IResult> (
            CreateCategoryCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var cat = await catalogService.CreateCategoryAsync(accessor.Current!.StoreId, command, ct);
            return TypedResults.Created($"/api/stores/{accessor.Current.StoreSlug}/catalog/categories", cat);
        });

        group.MapPut("/categories/{categoryId:guid}", async Task<IResult> (
            Guid categoryId,
            UpdateCategoryCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var cat = await catalogService.UpdateCategoryAsync(accessor.Current!.StoreId, categoryId, command, ct);
            return cat is null ? TypedResults.NotFound() : TypedResults.Ok(cat);
        });

        group.MapPost("/products", async Task<IResult> (
            CreateProductCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var product = await catalogService.CreateProductAsync(accessor.Current!.StoreId, command, ct);
            return TypedResults.Created($"/api/stores/{accessor.Current.StoreSlug}/catalog/products/{product.Id}", product);
        });

        group.MapPut("/products/{productId:guid}", async Task<IResult> (
            Guid productId,
            UpdateProductCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var product = await catalogService.UpdateProductAsync(accessor.Current!.StoreId, productId, command, ct);
            return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
        });

        group.MapPost("/products/{productId:guid}/publish", async Task<IResult> (
            Guid productId,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var ok = await catalogService.PublishProductAsync(accessor.Current!.StoreId, productId, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPost("/products/{productId:guid}/archive", async Task<IResult> (
            Guid productId,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var ok = await catalogService.ArchiveProductAsync(accessor.Current!.StoreId, productId, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPut("/products/{productId:guid}/inventory", async Task<IResult> (
            Guid productId,
            SetInventoryRequest request,
            HttpContext httpContext,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            if (request.OnHand < 0)
            {
                return TypedResults.ValidationProblem(
                    errors: new Dictionary<string, string[]>
                    {
                        ["OnHand"] = ["OnHand cannot be negative."]
                    },
                    detail: "One or more validation errors occurred.",
                    instance: httpContext.Request.Path,
                    title: "Validation Failed",
                    type: "about:blank",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "validation_error",
                        ["traceId"] = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier
                    });
            }

            var ok = await catalogService.SetInventoryOnHandAsync(accessor.Current!.StoreId, productId, request.OnHand, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapPost("/products/{productId:guid}/images", async Task<IResult> (
            Guid productId,
            AddProductImageCommand command,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var img = await catalogService.AddProductImageAsync(accessor.Current!.StoreId, productId, command, ct);
            return img is null ? TypedResults.NotFound() : TypedResults.Created($"/api/stores/{accessor.Current.StoreSlug}/catalog/products/{productId}", img);
        });

        group.MapDelete("/products/{productId:guid}/images/{imageId:guid}", async Task<IResult> (
            Guid productId,
            Guid imageId,
            IStoreRequestContextAccessor accessor,
            IMerchantCatalogService catalogService,
            CancellationToken ct) =>
        {
            var ok = await catalogService.RemoveProductImageAsync(accessor.Current!.StoreId, productId, imageId, ct);
            return ok ? TypedResults.NoContent() : TypedResults.NotFound();
        });

        group.MapGet("/orders", async Task<IResult> (
            int? page,
            int? pageSize,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var orders = await orderService.GetMerchantOrdersAsync(
                accessor.Current!.StoreId,
                page ?? 1,
                pageSize ?? 20,
                ct);
            return TypedResults.Ok(orders);
        });

        group.MapPost("/orders/{orderId:guid}/confirm", async Task<IResult> (
            Guid orderId,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var actorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var order = await orderService.ConfirmOrderAsync(accessor.Current!.StoreId, actorId, orderId, ct);
            return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
        });

        group.MapPost("/orders/{orderId:guid}/reject", async Task<IResult> (
            Guid orderId,
            RejectOrderRequest request,
            HttpContext httpContext,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                return TypedResults.ValidationProblem(
                    errors: new Dictionary<string, string[]>
                    {
                        ["Reason"] = ["Rejection reason is required."]
                    },
                    detail: "One or more validation errors occurred.",
                    instance: httpContext.Request.Path,
                    title: "Validation Failed",
                    type: "about:blank",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "validation_error",
                        ["traceId"] = System.Diagnostics.Activity.Current?.Id ?? httpContext.TraceIdentifier
                    });
            }

            var actorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var order = await orderService.RejectOrderAsync(accessor.Current!.StoreId, actorId, orderId, request.Reason, ct);
            return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
        });

        group.MapPost("/orders/{orderId:guid}/fulfillment", async Task<IResult> (
            Guid orderId,
            AdvanceFulfillmentRequest request,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var actorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var order = await orderService.AdvanceFulfillmentAsync(accessor.Current!.StoreId, actorId, orderId, request.NextStatus, ct);
            return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
        });

        group.MapPost("/orders/{orderId:guid}/collect-payment", async Task<IResult> (
            Guid orderId,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var actorId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var order = await orderService.CollectCodPaymentAsync(accessor.Current!.StoreId, actorId, orderId, ct);
            return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
        });

        return app;
    }
}

public sealed class TenantClaimMatchEndpointFilter : IEndpointFilter
{
    private readonly IStoreRequestContextAccessor _storeContextAccessor;

    public TenantClaimMatchEndpointFilter(IStoreRequestContextAccessor storeContextAccessor)
    {
        _storeContextAccessor = storeContextAccessor;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var store = _storeContextAccessor.Current;
        var claimStoreId = context.HttpContext.User.FindFirstValue("store_id");

        if (store is null || !Guid.TryParse(claimStoreId, out var userStoreId) || userStoreId != store.StoreId)
        {
            return TypedResults.Forbid();
        }

        return await next(context);
    }
}
