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
            HttpResponse response,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var cart = await cartService.GetCartAsync(accessor.Current!.StoreId, customerId, ct);
            SetCartETag(response, cart.Version);
            return TypedResults.Ok(cart);
        });

        group.MapPut("/cart/items", async Task<IResult> (
            UpsertCartItemCommand command,
            HttpRequest request,
            HttpResponse response,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var expectedVersion = ParseIfMatchVersion(request);
            try
            {
                var cart = await cartService.UpsertItemAsync(accessor.Current!.StoreId, customerId, command, ct, expectedVersion);
                SetCartETag(response, cart.Version);
                return TypedResults.Ok(cart);
            }
            catch (CartConcurrencyConflictException ex)
            {
                return MapCartConflict(ex);
            }
        });

        group.MapDelete("/cart/items/{productId:guid}", async Task<IResult> (
            Guid productId,
            HttpRequest request,
            HttpResponse response,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var expectedVersion = ParseIfMatchVersion(request);
            try
            {
                var cart = await cartService.RemoveItemAsync(accessor.Current!.StoreId, customerId, productId, ct, expectedVersion);
                SetCartETag(response, cart.Version);
                return TypedResults.Ok(cart);
            }
            catch (CartConcurrencyConflictException ex)
            {
                return MapCartConflict(ex);
            }
        });

        group.MapDelete("/cart", async Task<IResult> (
            HttpRequest request,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            ICartService cartService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var expectedVersion = ParseIfMatchVersion(request);
            try
            {
                await cartService.ClearCartAsync(accessor.Current!.StoreId, customerId, ct, expectedVersion);
                return TypedResults.NoContent();
            }
            catch (CartConcurrencyConflictException ex)
            {
                return MapCartConflict(ex);
            }
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
            int? page,
            int? pageSize,
            ClaimsPrincipal user,
            IStoreRequestContextAccessor accessor,
            IOrderService orderService,
            CancellationToken ct) =>
        {
            var customerId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var orders = await orderService.GetCustomerOrdersAsync(
                accessor.Current!.StoreId,
                customerId,
                page ?? 1,
                pageSize ?? 20,
                ct);
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

    private static void SetCartETag(HttpResponse response, string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            response.Headers.ETag = $"\"{version}\"";
        }
    }

    private static string? ParseIfMatchVersion(HttpRequest request)
    {
        var raw = request.Headers.IfMatch.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (string.Equals(trimmed, "*", StringComparison.Ordinal))
        {
            return "*";
        }

        if (trimmed.StartsWith("W/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[2..].Trim();
        }

        return trimmed.Trim('"');
    }

    private static IResult MapCartConflict(CartConcurrencyConflictException ex)
    {
        var status = ex.IsPreconditionFailed
            ? StatusCodes.Status412PreconditionFailed
            : StatusCodes.Status409Conflict;

        return TypedResults.Problem(
            statusCode: status,
            title: ex.IsPreconditionFailed ? "Precondition Failed" : "Cart Concurrency Conflict",
            detail: ex.Message,
            extensions: new Dictionary<string, object?> { ["code"] = "cart_concurrency_conflict" });
    }
}
