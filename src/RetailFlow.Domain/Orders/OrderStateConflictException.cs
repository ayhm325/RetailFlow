namespace RetailFlow.Domain.Orders;

public sealed class OrderStateConflictException : InvalidOperationException
{
    public OrderStateConflictException(string message)
        : base(message)
    {
    }
}
