using RetailFlow.Domain.Orders;

namespace RetailFlow.Application.Orders;

public sealed record CartItemDto(
    Guid ProductId,
    string ProductName,
    string? Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal,
    bool IsAvailable);

public sealed record CartDto(
    Guid StoreId,
    Guid CustomerId,
    string CurrencyCode,
    decimal Subtotal,
    IReadOnlyList<CartItemDto> Items);

public sealed record UpsertCartItemCommand(Guid ProductId, int Quantity);

public sealed record CheckoutCommand(
    FulfillmentMode FulfillmentMode,
    Guid? DeliveryZoneId,
    string? DeliveryAddress);

public enum CheckoutOutcomeStatus
{
    Created = 1,
    Replayed = 2,
    ConcurrentInFlight = 3,
    PayloadMismatch = 4,
    ValidationFailed = 5
}

public sealed record CheckoutResult(
    CheckoutOutcomeStatus Status,
    OrderDto? Order,
    string? ErrorDetail = null);

public sealed record OrderItemDto(
    Guid ProductId,
    string ProductName,
    string? Sku,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

public sealed record OrderDto(
    Guid Id,
    Guid StoreId,
    Guid CustomerId,
    string OrderNumber,
    FulfillmentMode FulfillmentMode,
    OrderStatus OrderStatus,
    FulfillmentStatus FulfillmentStatus,
    PaymentStatus PaymentStatus,
    string CurrencyCode,
    decimal Subtotal,
    decimal DeliveryFee,
    decimal Total,
    Guid? DeliveryZoneId,
    string? DeliveryZoneName,
    string? DeliveryAddress,
    string? CancellationReason,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ReservationExpiresAtUtc,
    IReadOnlyList<OrderItemDto> Items);

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid storeId, Guid customerId, CancellationToken cancellationToken);
    Task<CartDto> UpsertItemAsync(Guid storeId, Guid customerId, UpsertCartItemCommand command, CancellationToken cancellationToken);
    Task<CartDto> RemoveItemAsync(Guid storeId, Guid customerId, Guid productId, CancellationToken cancellationToken);
    Task ClearCartAsync(Guid storeId, Guid customerId, CancellationToken cancellationToken);
}

public interface ICheckoutService
{
    Task<CheckoutResult> CheckoutAsync(
        Guid storeId,
        Guid customerId,
        string idempotencyKey,
        CheckoutCommand command,
        CancellationToken cancellationToken);
}

public sealed record PagedOrdersResponse(
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<OrderDto> Items);

public interface IOrderService
{
    Task<PagedOrdersResponse> GetCustomerOrdersAsync(Guid storeId, Guid customerId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetCustomerOrderByIdAsync(Guid storeId, Guid customerId, Guid orderId, CancellationToken cancellationToken);
    Task<OrderDto?> CancelByCustomerAsync(Guid storeId, Guid customerId, Guid orderId, string? reason, CancellationToken cancellationToken);

    Task<PagedOrdersResponse> GetMerchantOrdersAsync(Guid storeId, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<OrderDto?> ConfirmOrderAsync(Guid storeId, Guid actorUserId, Guid orderId, CancellationToken cancellationToken);
    Task<OrderDto?> RejectOrderAsync(Guid storeId, Guid actorUserId, Guid orderId, string reason, CancellationToken cancellationToken);
    Task<OrderDto?> AdvanceFulfillmentAsync(Guid storeId, Guid actorUserId, Guid orderId, FulfillmentStatus nextStatus, CancellationToken cancellationToken);
    Task<OrderDto?> CollectCodPaymentAsync(Guid storeId, Guid actorUserId, Guid orderId, CancellationToken cancellationToken);

    Task<int> ReleaseExpiredReservationsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
}
