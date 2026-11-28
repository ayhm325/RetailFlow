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
    IReadOnlyList<CartItemDto> Items,
    string Version = "");

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
    ValidationFailed = 5,
    BusinessRuleFailed = 6,
    ConcurrencyConflict = 7
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
    decimal LineTotal,
    InventoryReservationStatus ReservationStatus = InventoryReservationStatus.Active,
    Guid OrderItemId = default);

public sealed record FulfillmentAttemptItemDto(
    Guid Id,
    Guid OrderItemId,
    Guid ProductId,
    int Quantity,
    ReturnDisposition ReturnDisposition,
    DateTimeOffset? ReturnedAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    Guid? ResolvedByUserId,
    string? ResolutionNote);

public sealed record FulfillmentAttemptDto(
    Guid Id,
    int? AttemptNumber,
    FulfillmentAttemptStatus Status,
    bool IsActive,
    DateTimeOffset PreparedAtUtc,
    DateTimeOffset? DispatchedAtUtc,
    DateTimeOffset? DeliveredAtUtc,
    DateTimeOffset? FailedAtUtc,
    DateTimeOffset? CancelledAtUtc,
    DeliveryFailureReasonCode? FailureReasonCode,
    DeliveryFailureCategory? FailureCategory,
    string? FailureNote,
    DateTimeOffset? ResolvedAtUtc,
    Guid? ResolvedByUserId,
    string? ResolutionNote,
    IReadOnlyList<FulfillmentAttemptItemDto> Items);

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
    IReadOnlyList<OrderItemDto> Items,
    OrderReservationSummary ReservationSummary = OrderReservationSummary.Active,
    string Version = "",
    IReadOnlyList<FulfillmentAttemptDto>? FulfillmentAttempts = null);

public sealed record OrderItemDispositionCommand(
    Guid OrderItemId,
    ReturnDisposition Disposition,
    string? Note);

public sealed class OrderPreconditionException : Exception
{
    public bool IsMissingPrecondition { get; }

    public OrderPreconditionException(string message, bool isMissingPrecondition, Exception? innerException = null)
        : base(message, innerException)
    {
        IsMissingPrecondition = isMissingPrecondition;
    }
}

public sealed class OrderIdempotencyPayloadMismatchException : Exception
{
    public OrderIdempotencyPayloadMismatchException(string message)
        : base(message)
    {
    }
}

public sealed class CartConcurrencyConflictException : Exception
{
    public bool IsPreconditionFailed { get; }

    public CartConcurrencyConflictException(string message, bool isPreconditionFailed, Exception? innerException = null)
        : base(message, innerException)
    {
        IsPreconditionFailed = isPreconditionFailed;
    }
}

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid storeId, Guid customerId, CancellationToken cancellationToken);
    Task<CartDto> UpsertItemAsync(Guid storeId, Guid customerId, UpsertCartItemCommand command, CancellationToken cancellationToken, string? expectedVersion = null);
    Task<CartDto> RemoveItemAsync(Guid storeId, Guid customerId, Guid productId, CancellationToken cancellationToken, string? expectedVersion = null);
    Task ClearCartAsync(Guid storeId, Guid customerId, CancellationToken cancellationToken, string? expectedVersion = null);
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
    Task<OrderDto?> GetMerchantOrderByIdAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken);
    Task<OrderDto?> ConfirmOrderAsync(Guid storeId, Guid actorUserId, Guid orderId, CancellationToken cancellationToken);
    Task<OrderDto?> RejectOrderAsync(Guid storeId, Guid actorUserId, Guid orderId, string reason, CancellationToken cancellationToken);
    Task<OrderDto?> AdvanceFulfillmentAsync(Guid storeId, Guid actorUserId, Guid orderId, FulfillmentStatus nextStatus, CancellationToken cancellationToken, DeliveryFailureReasonCode? failureReasonCode = null, string? failureNote = null);
    Task<OrderDto?> CollectCodPaymentAsync(Guid storeId, Guid actorUserId, Guid orderId, CancellationToken cancellationToken);
    Task<OrderDto?> ResolveDeliveryCorrectionAsync(Guid storeId, Guid actorUserId, Guid orderId, string resolutionNote, string? expectedVersion, CancellationToken cancellationToken);
    Task<OrderDto?> RetryDeliveryAsync(Guid storeId, Guid actorUserId, Guid orderId, string idempotencyKey, string? note, string? expectedVersion, CancellationToken cancellationToken);
    Task<OrderDto?> CancelByMerchantAsync(Guid storeId, Guid actorUserId, Guid orderId, string reason, string? expectedVersion, CancellationToken cancellationToken);
    Task<OrderDto?> ResolveInventoryDispositionAsync(Guid storeId, Guid actorUserId, Guid orderId, Guid fulfillmentAttemptId, IReadOnlyList<OrderItemDispositionCommand> items, string? expectedVersion, CancellationToken cancellationToken);

    Task<int> ReleaseExpiredReservationsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken);
}
