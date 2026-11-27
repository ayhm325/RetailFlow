namespace RetailFlow.Application.Orders;

public sealed class OrderConcurrencyConflictException : Exception
{
    public Guid OrderId { get; }

    public string Operation { get; }

    public OrderConcurrencyConflictException(
        Guid orderId,
        string operation,
        Exception innerException)
        : base(
            "The order or related inventory was modified concurrently.",
            innerException)
    {
        OrderId = orderId;
        Operation = operation;
    }
}
