namespace RetailFlow.Domain.Inventory;

public sealed class InventoryOnHandBelowReservedException : InvalidOperationException
{
    public Guid InventoryItemId { get; }
    public Guid ProductId { get; }
    public int RequestedOnHand { get; }
    public int ReservedQuantity { get; }

    public InventoryOnHandBelowReservedException(
        Guid inventoryItemId,
        Guid productId,
        int requestedOnHand,
        int reservedQuantity)
        : base($"OnHand ({requestedOnHand}) cannot be less than Reserved ({reservedQuantity}).")
    {
        InventoryItemId = inventoryItemId;
        ProductId = productId;
        RequestedOnHand = requestedOnHand;
        ReservedQuantity = reservedQuantity;
    }
}
