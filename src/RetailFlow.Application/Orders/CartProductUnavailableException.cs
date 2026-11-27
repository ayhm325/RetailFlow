namespace RetailFlow.Application.Orders;

public sealed class CartProductUnavailableException : Exception
{
    public CartProductUnavailableException()
        : base("The requested product is unavailable for this store.")
    {
    }
}
