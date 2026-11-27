namespace RetailFlow.Application.Catalog;

public sealed class InventoryConcurrencyConflictException : Exception
{
    public Guid StoreId { get; }
    public Guid ProductId { get; }
    public string Operation { get; }

    public InventoryConcurrencyConflictException(
        Guid storeId,
        Guid productId,
        string operation,
        Exception innerException)
        : base("Inventory was modified concurrently.", innerException)
    {
        StoreId = storeId;
        ProductId = productId;
        Operation = operation;
    }
}
