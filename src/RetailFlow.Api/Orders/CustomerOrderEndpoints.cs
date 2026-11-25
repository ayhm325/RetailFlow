using System.Security.Claims;

using RetailFlow.Api.Catalog;
using RetailFlow.Api.Merchant;
using RetailFlow.Application.Orders;
using RetailFlow.Application.Stores;

namespace RetailFlow.Api.Orders;

public static class CustomerOrderEndpoints
{
    public sealed record CancelOrderRequest(string? Reason);

    public static WebApplication MapCustomerOrderEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/stores/{slug}")
            .RequireAuthorization("Customer")
            .AddEndpointFilter<StoreResolutionEndpointFilter>()
            .AddEndpointFilter<TenantClaimMatchEndpointFilter>();

        group.MapGet("/cart", async Task<IResult> (
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var cart = await cartService.GetCartAsync(accessor.Current!.StoreId, customerId, ct);
            return TypedResults.Ok(cart);
        });

        group.MapPut("/cart/items", async Task<IResult> (
            UpsertCartItemCommand command,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var cart = await cartService.UpsertItemAsync(accessor.Current!.StoreId, customerId, command, ct);
            return TypedResults.Ok(cart);
        });

        group.MapDelete("/cart/items/{productId:guid}", async Task<IResult> (
            Guid productId,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var cart = await cartService.RemoveItemAsync(accessor.Current!.StoreId, customerId, productId, ct);
            return TypedResults.Ok(cart);
        });

        group.MapDelete("/cart", async Task<IResult> (
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await cartService.ClearCartAsync(accessor.Current!.StoreId, customerId, ct);
            return TypedResults.NoContent();
        });

        group.MapPost("/checkout", async Task<IResult> (
            CheckoutCommand command,
            HttpRequest request,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICheckoutService checkoutService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var idempotencyKey = request.Headers["Idempotency-Key"].FirstOrDefault() ?? string.Empty;

            var result = await checkoutService.CheckoutAsync(
                accessor.Current!.StoreId,
                customerId,
                idempotencyKey,
                command,
                ct);

            return result.Status switch
            {
                CheckoutOutcomeStatus.Created => TypedResults.Created($"/api/stores/{accessor.Current.StoreSlug}/orders/{result.Order!.Id}", result.Order),
                CheckoutOutcomeStatus.Replayed => TypedResults.Ok(result.Order),
                CheckoutOutcomeStatus.ConcurrentInFlight => TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Conflict", detail: result.ErrorDetail),
                CheckoutOutcomeStatus.PayloadMismatch => TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Idempotency Key Payload Mismatch", detail: result.ErrorDetail),
                _ => TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Checkout Failed", detail: result.ErrorDetail)
            };
        });

        group.MapGet("/orders", async Task<IResult> (
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var orders = await orderService.GetCustomerOrdersAsync(accessor.Current!.StoreId, customerId, ct);
            return TypedResults.Ok(orders);
        });

        group.MapGet("/orders/{orderId:guid}", async Task<IResult> (
            Guid orderId,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var order = await orderService.GetCustomerOrderByIdAsync(accessor.Current!.StoreId, customerId, orderId, ct);
            return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
        });

        group.MapPost("/orders/{orderId:guid}/cancel", async Task<IResult> (
            Guid orderId,
            HttpRequest httpRequest,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            CancelOrderRequest? request = null;
            if (httpRequest.ContentLength is > 0)
            {
                request = await httpRequest.ReadFromJsonAsync<CancelOrderRequest>(ct);
            }

            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            try
            {
                var order = await orderService.CancelByCustomerAsync(accessor.Current!.StoreId, customerId, orderId, request?.Reason, ct);
                return order is null ? TypedResults.NotFound() : TypedResults.Ok(order);
            }
            catch (InvalidOperationException ex)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "Cancellation Not Allowed", detail: ex.Message);
            }
        });

        return app;
    }
}
