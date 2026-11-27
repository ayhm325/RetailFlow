namespace RetailFlow.Domain.Inventory;

public sealed class InventoryItem
{
    private InventoryItem()
    {
        RowVersion = [];
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid ProductId { get; private set; }

    public int OnHand { get; private set; }

    public int Reserved { get; private set; }

    public byte[] RowVersion { get; private set; }

    public int Available => OnHand - Reserved;

    public bool IsAvailable => Available > 0;

    public static InventoryItem Create(
        Guid id,
        Guid storeId,
        Guid productId,
        int onHand,
        int reserved)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("InventoryItem id cannot be empty.", nameof(id));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        }

        if (onHand < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(onHand),
                "OnHand cannot be negative.");
        }

        if (reserved < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserved),
                "Reserved cannot be negative.");
        }

        if (reserved > onHand)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reserved),
                "Reserved cannot exceed OnHand.");
        }

        return new InventoryItem
        {
            Id = id,
            StoreId = storeId,
            ProductId = productId,
            OnHand = onHand,
            Reserved = reserved
        };
    }

    public void SetOnHand(int newOnHand)
    {
        if (newOnHand < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(newOnHand),
                "OnHand cannot be negative.");
        }

        if (newOnHand < Reserved)
        {
            throw new InventoryOnHandBelowReservedException(Id, ProductId, newOnHand, Reserved);
        }

        OnHand = newOnHand;
    }

    public void AdjustOnHand(int delta)
    {
        var updated = checked(OnHand + delta);
        SetOnHand(updated);
    }

    public void Reserve(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Reservation quantity must be positive.");
        }

        if (Available < quantity)
        {
            throw new InvalidOperationException(
                $"Insufficient available inventory. Available: {Available}, Requested: {quantity}.");
        }

        Reserved = checked(Reserved + quantity);
    }

    public void ReleaseReservation(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Release quantity must be positive.");
        }

        if (Reserved < quantity)
        {
            throw new InvalidOperationException(
                $"Cannot release {quantity} units; only {Reserved} units are reserved.");
        }

        Reserved -= quantity;
    }

    public void CommitReservation(int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Commit quantity must be positive.");
        }

        if (Reserved < quantity || OnHand < quantity)
        {
            throw new InvalidOperationException(
                $"Cannot commit {quantity} units (OnHand={OnHand}, Reserved={Reserved}).");
        }

        Reserved -= quantity;
        OnHand -= quantity;
    }
}
