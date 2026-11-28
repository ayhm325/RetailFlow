using RetailFlow.Domain.Orders;

namespace RetailFlow.Domain.Inventory;

public enum InventoryAdjustmentOperationType
{
    WriteOffDamaged = 1,
    WriteOffLost = 2
}

public sealed class InventoryAdjustment
{
    private InventoryAdjustment()
    {
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public Guid? FulfillmentAttemptItemId { get; private set; }
    public InventoryAdjustmentOperationType OperationType { get; private set; }
    public int OnHandDelta { get; private set; }
    public int ReservedDelta { get; private set; }
    public DeliveryFailureReasonCode? ReasonCode { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string? Note { get; private set; }

    public static InventoryAdjustment CreateWriteOff(
        Guid id,
        Guid storeId,
        Guid productId,
        Guid orderId,
        Guid orderItemId,
        Guid? fulfillmentAttemptItemId,
        InventoryAdjustmentOperationType operationType,
        int quantity,
        DeliveryFailureReasonCode? reasonCode,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        string? note = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id cannot be empty.", nameof(id));
        if (storeId == Guid.Empty) throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        if (productId == Guid.Empty) throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        if (orderId == Guid.Empty) throw new ArgumentException("OrderId cannot be empty.", nameof(orderId));
        if (orderItemId == Guid.Empty) throw new ArgumentException("OrderItemId cannot be empty.", nameof(orderItemId));
        if (actorUserId == Guid.Empty) throw new ArgumentException("ActorUserId cannot be empty.", nameof(actorUserId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Write-off quantity must be positive.");
        if (operationType is not (InventoryAdjustmentOperationType.WriteOffDamaged or InventoryAdjustmentOperationType.WriteOffLost))
        {
            throw new ArgumentOutOfRangeException(nameof(operationType), "OperationType must be WriteOffDamaged or WriteOffLost.");
        }

        return new InventoryAdjustment
        {
            Id = id,
            StoreId = storeId,
            ProductId = productId,
            OrderId = orderId,
            OrderItemId = orderItemId,
            FulfillmentAttemptItemId = fulfillmentAttemptItemId,
            OperationType = operationType,
            OnHandDelta = -quantity,
            ReservedDelta = -quantity,
            ReasonCode = reasonCode,
            ActorUserId = actorUserId,
            OccurredAtUtc = occurredAtUtc,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()
        };
    }
}
