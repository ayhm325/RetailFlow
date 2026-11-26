namespace RetailFlow.Domain.Orders;

public sealed class Cart
{
    private readonly List<CartItem> _items = [];

    private Cart()
    {
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<CartItem> Items => _items;

    public static Cart Create(Guid id, Guid storeId, Guid customerId, DateTimeOffset updatedAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Cart id cannot be empty.", nameof(id));
        if (storeId == Guid.Empty) throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId cannot be empty.", nameof(customerId));

        return new Cart
        {
            Id = id,
            StoreId = storeId,
            CustomerId = customerId,
            UpdatedAtUtc = updatedAtUtc
        };
    }

    public void Touch(DateTimeOffset updatedAtUtc) => UpdatedAtUtc = updatedAtUtc;
}

public sealed class CartItem
{
    private CartItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CartId { get; private set; }

    public Guid ProductId { get; private set; }

    public int Quantity { get; private set; }

    public static CartItem Create(Guid id, Guid storeId, Guid cartId, Guid productId, int quantity)
    {
        if (id == Guid.Empty) throw new ArgumentException("CartItem id cannot be empty.", nameof(id));
        if (storeId == Guid.Empty) throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        if (cartId == Guid.Empty) throw new ArgumentException("CartId cannot be empty.", nameof(cartId));
        if (productId == Guid.Empty) throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");

        return new CartItem
        {
            Id = id,
            StoreId = storeId,
            CartId = cartId,
            ProductId = productId,
            Quantity = quantity
        };
    }

    public void SetQuantity(int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        Quantity = quantity;
    }
}
