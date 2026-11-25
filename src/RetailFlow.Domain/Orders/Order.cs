using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Domain.Orders;

/// <summary>
/// Multi-tenant Order aggregate enforcing ADR-0005 state machines and OD-001/OD-002 policies.
/// </summary>
public sealed class Order
{
    public const int ReservationTimeoutMinutes = 15; // OD-001 (Approved 2026-11-26)

    private readonly List<OrderItem> _items = [];
    private readonly List<OrderStatusHistory> _history = [];

    private Order()
    {
        OrderNumber = string.Empty;
        CurrencyCode = string.Empty;
        RowVersion = [];
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string OrderNumber { get; private set; }
    public FulfillmentMode FulfillmentMode { get; private set; }
    public OrderStatus OrderStatus { get; private set; }
    public FulfillmentStatus FulfillmentStatus { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public string CurrencyCode { get; private set; }
    public decimal Subtotal { get; private set; }
    public decimal DeliveryFee { get; private set; }
    public decimal Total { get; private set; }
    public Guid? DeliveryZoneId { get; private set; }
    public string? DeliveryZoneName { get; private set; }
    public string? DeliveryAddress { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ReservationExpiresAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; }

    public IReadOnlyCollection<OrderItem> Items => _items;
    public IReadOnlyCollection<OrderStatusHistory> History => _history;

    public static Order Create(
        Guid id,
        Store store,
        StoreSettings settings,
        Currency currency,
        Guid customerId,
        string orderNumber,
        FulfillmentMode fulfillmentMode,
        IReadOnlyList<OrderItem> items,
        decimal deliveryFee,
        Guid? deliveryZoneId,
        string? deliveryZoneName,
        string? deliveryAddress,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Order id cannot be empty.", nameof(id));
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(currency);
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId cannot be empty.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(orderNumber)) throw new ArgumentException("OrderNumber is required.", nameof(orderNumber));
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0) throw new ArgumentException("Order must contain at least one item.", nameof(items));

        if (!store.IsPubliclyAccessible)
        {
            throw new InvalidOperationException("Orders can only be placed in an active store.");
        }

        if (fulfillmentMode == FulfillmentMode.Delivery)
        {
            if (!settings.DeliveryEnabled)
            {
                throw new InvalidOperationException("Delivery is not enabled for this store.");
            }

            if (deliveryZoneId is null || deliveryZoneId == Guid.Empty)
            {
                throw new ArgumentException("DeliveryZoneId is required for delivery orders.", nameof(deliveryZoneId));
            }

            if (string.IsNullOrWhiteSpace(deliveryAddress))
            {
                throw new ArgumentException("DeliveryAddress is required for delivery orders.", nameof(deliveryAddress));
            }
        }
        else
        {
            if (store.StoreType != StoreType.Physical || !settings.PickupEnabled)
            {
                throw new InvalidOperationException("Pickup is not enabled for this store.");
            }

            if (deliveryFee != 0m)
            {
                throw new ArgumentException("Pickup orders must have zero delivery fee.", nameof(deliveryFee));
            }
        }

        MoneyPrecision.EnsureValidAmount(deliveryFee, currency.MinorUnit, nameof(deliveryFee));

        decimal subtotal = 0m;
        foreach (var item in items)
        {
            if (item.StoreId != store.Id || item.OrderId != id)
            {
                throw new InvalidOperationException("OrderItem tenant/order mismatch.");
            }

            subtotal += item.LineTotal;
        }

        MoneyPrecision.EnsureValidAmount(subtotal, currency.MinorUnit, nameof(items));
        var total = subtotal + deliveryFee;

        var order = new Order
        {
            Id = id,
            StoreId = store.Id,
            CustomerId = customerId,
            OrderNumber = orderNumber.Trim(),
            FulfillmentMode = fulfillmentMode,
            OrderStatus = OrderStatus.Pending,
            FulfillmentStatus = FulfillmentStatus.Unfulfilled,
            PaymentStatus = PaymentStatus.Pending,
            CurrencyCode = currency.Code,
            Subtotal = subtotal,
            DeliveryFee = deliveryFee,
            Total = total,
            DeliveryZoneId = fulfillmentMode == FulfillmentMode.Delivery ? deliveryZoneId : null,
            DeliveryZoneName = fulfillmentMode == FulfillmentMode.Delivery ? deliveryZoneName?.Trim() : null,
            DeliveryAddress = fulfillmentMode == FulfillmentMode.Delivery ? deliveryAddress?.Trim() : null,
            CreatedAtUtc = createdAtUtc,
            ReservationExpiresAtUtc = createdAtUtc.AddMinutes(ReservationTimeoutMinutes)
        };

        order._items.AddRange(items);
        order._history.Add(OrderStatusHistory.Create(
            Guid.NewGuid(),
            store.Id,
            id,
            customerId,
            "Customer",
            null,
            OrderStatus.Pending,
            null,
            FulfillmentStatus.Unfulfilled,
            "Order placed",
            createdAtUtc));

        return order;
    }

    public void Confirm(Guid actorUserId, string actorRole, DateTimeOffset occurredAtUtc)
    {
        if (OrderStatus != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot confirm order from status '{OrderStatus}'.");
        }

        var prevOrder = OrderStatus;
        OrderStatus = OrderStatus.Confirmed;
        RecordHistory(actorUserId, actorRole, prevOrder, OrderStatus, FulfillmentStatus, FulfillmentStatus, "Order confirmed", occurredAtUtc);
    }

    public void Reject(Guid actorUserId, string actorRole, string reason, DateTimeOffset occurredAtUtc)
    {
        if (OrderStatus != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot reject order from status '{OrderStatus}'.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Rejection reason is required.", nameof(reason));
        }

        var prevOrder = OrderStatus;
        var prevFulfillment = FulfillmentStatus;
        OrderStatus = OrderStatus.Rejected;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        PaymentStatus = PaymentStatus.Voided;
        CancellationReason = reason.Trim();
        RecordHistory(actorUserId, actorRole, prevOrder, OrderStatus, prevFulfillment, FulfillmentStatus, CancellationReason, occurredAtUtc);
    }

    /// <summary>
    /// OD-002 (Approved 2026-11-26): Customer self-cancellation is allowed ONLY while OrderStatus == Pending.
    /// </summary>
    public void CancelByCustomer(Guid customerId, string? reason, DateTimeOffset occurredAtUtc)
    {
        if (customerId != CustomerId)
        {
            throw new InvalidOperationException("Only the owning customer may cancel this order.");
        }

        if (OrderStatus != OrderStatus.Pending)
        {
            throw new InvalidOperationException($"Customer self-cancellation is only allowed in Pending status (current: '{OrderStatus}').");
        }

        var prevOrder = OrderStatus;
        var prevFulfillment = FulfillmentStatus;
        OrderStatus = OrderStatus.Cancelled;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        PaymentStatus = PaymentStatus.Voided;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled by customer" : reason.Trim();
        RecordHistory(customerId, "Customer", prevOrder, OrderStatus, prevFulfillment, FulfillmentStatus, CancellationReason, occurredAtUtc);
    }

    public void CancelByMerchantOrTimeout(Guid? actorUserId, string actorRole, string reason, DateTimeOffset occurredAtUtc)
    {
        if (OrderStatus != OrderStatus.Pending && OrderStatus != OrderStatus.Confirmed)
        {
            throw new InvalidOperationException($"Cannot cancel order from status '{OrderStatus}'.");
        }

        if (FulfillmentStatus is FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp)
        {
            throw new InvalidOperationException("Cannot cancel an order that has already been delivered or picked up.");
        }

        var prevOrder = OrderStatus;
        var prevFulfillment = FulfillmentStatus;
        OrderStatus = OrderStatus.Cancelled;
        FulfillmentStatus = FulfillmentStatus.Cancelled;
        PaymentStatus = PaymentStatus.Voided;
        CancellationReason = string.IsNullOrWhiteSpace(reason) ? "Cancelled" : reason.Trim();
        RecordHistory(actorUserId, actorRole, prevOrder, OrderStatus, prevFulfillment, FulfillmentStatus, CancellationReason, occurredAtUtc);
    }

    public void AdvanceFulfillment(Guid actorUserId, string actorRole, FulfillmentStatus nextStatus, DateTimeOffset occurredAtUtc)
    {
        if (OrderStatus != OrderStatus.Confirmed)
        {
            throw new InvalidOperationException($"Fulfillment can only advance when OrderStatus is Confirmed (current: '{OrderStatus}').");
        }

        var valid = (FulfillmentMode, FulfillmentStatus, nextStatus) switch
        {
            (_, FulfillmentStatus.Unfulfilled, FulfillmentStatus.Preparing) => true,
            (FulfillmentMode.Delivery, FulfillmentStatus.Preparing, FulfillmentStatus.OutForDelivery) => true,
            (FulfillmentMode.Delivery, FulfillmentStatus.OutForDelivery, FulfillmentStatus.Delivered) => true,
            (FulfillmentMode.Delivery, FulfillmentStatus.OutForDelivery, FulfillmentStatus.Failed) => true,
            (FulfillmentMode.Pickup, FulfillmentStatus.Preparing, FulfillmentStatus.ReadyForPickup) => true,
            (FulfillmentMode.Pickup, FulfillmentStatus.ReadyForPickup, FulfillmentStatus.PickedUp) => true,
            _ => false
        };

        if (!valid)
        {
            throw new InvalidOperationException(
                $"Invalid fulfillment transition '{FulfillmentStatus}' -> '{nextStatus}' for mode '{FulfillmentMode}'.");
        }

        var prevFulfillment = FulfillmentStatus;
        FulfillmentStatus = nextStatus;
        RecordHistory(actorUserId, actorRole, OrderStatus, OrderStatus, prevFulfillment, nextStatus, $"Fulfillment -> {nextStatus}", occurredAtUtc);
    }

    public void CollectCodPayment(Guid actorUserId, string actorRole, DateTimeOffset occurredAtUtc)
    {
        if (OrderStatus != OrderStatus.Confirmed)
        {
            throw new InvalidOperationException($"Cannot collect payment when OrderStatus is '{OrderStatus}'.");
        }

        if (FulfillmentStatus is not (FulfillmentStatus.Delivered or FulfillmentStatus.PickedUp))
        {
            throw new InvalidOperationException(
                $"COD payment can only be collected after Delivered or PickedUp (current: '{FulfillmentStatus}').");
        }

        if (PaymentStatus != PaymentStatus.Pending)
        {
            throw new InvalidOperationException($"Payment is already '{PaymentStatus}'.");
        }

        var prevOrder = OrderStatus;
        PaymentStatus = PaymentStatus.Collected;
        OrderStatus = OrderStatus.Completed;
        RecordHistory(actorUserId, actorRole, prevOrder, OrderStatus, FulfillmentStatus, FulfillmentStatus, "COD payment collected; order completed", occurredAtUtc);
    }

    private void RecordHistory(
        Guid? actorUserId,
        string actorRole,
        OrderStatus? fromOrder,
        OrderStatus toOrder,
        FulfillmentStatus? fromFulfillment,
        FulfillmentStatus toFulfillment,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        _history.Add(OrderStatusHistory.Create(
            Guid.NewGuid(),
            StoreId,
            Id,
            actorUserId,
            actorRole,
            fromOrder,
            toOrder,
            fromFulfillment,
            toFulfillment,
            note,
            occurredAtUtc));
    }
}

public sealed class OrderItem
{
    private OrderItem()
    {
        ProductName = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; }
    public string? Sku { get; private set; }
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }
    public decimal LineTotal { get; private set; }

    public static OrderItem Create(
        Guid id,
        Guid storeId,
        Guid orderId,
        Guid productId,
        string productName,
        string? sku,
        decimal unitPrice,
        int quantity,
        Currency currency)
    {
        if (id == Guid.Empty) throw new ArgumentException("OrderItem id cannot be empty.", nameof(id));
        if (storeId == Guid.Empty) throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId cannot be empty.", nameof(orderId));
        if (productId == Guid.Empty) throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        if (string.IsNullOrWhiteSpace(productName)) throw new ArgumentException("ProductName is required.", nameof(productName));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        ArgumentNullException.ThrowIfNull(currency);
        MoneyPrecision.EnsureValidAmount(unitPrice, currency.MinorUnit, nameof(unitPrice));

        return new OrderItem
        {
            Id = id,
            StoreId = storeId,
            OrderId = orderId,
            ProductId = productId,
            ProductName = productName.Trim(),
            Sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim(),
            UnitPrice = unitPrice,
            Quantity = quantity,
            LineTotal = unitPrice * quantity
        };
    }
}

public sealed class OrderStatusHistory
{
    private OrderStatusHistory()
    {
        ActorRole = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public string ActorRole { get; private set; }
    public OrderStatus? FromOrderStatus { get; private set; }
    public OrderStatus ToOrderStatus { get; private set; }
    public FulfillmentStatus? FromFulfillmentStatus { get; private set; }
    public FulfillmentStatus ToFulfillmentStatus { get; private set; }
    public string? Note { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static OrderStatusHistory Create(
        Guid id,
        Guid storeId,
        Guid orderId,
        Guid? actorUserId,
        string actorRole,
        OrderStatus? fromOrderStatus,
        OrderStatus toOrderStatus,
        FulfillmentStatus? fromFulfillmentStatus,
        FulfillmentStatus toFulfillmentStatus,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        return new OrderStatusHistory
        {
            Id = id,
            StoreId = storeId,
            OrderId = orderId,
            ActorUserId = actorUserId,
            ActorRole = string.IsNullOrWhiteSpace(actorRole) ? "System" : actorRole.Trim(),
            FromOrderStatus = fromOrderStatus,
            ToOrderStatus = toOrderStatus,
            FromFulfillmentStatus = fromFulfillmentStatus,
            ToFulfillmentStatus = toFulfillmentStatus,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }
}
