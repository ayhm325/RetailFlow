using RetailFlow.Domain.Inventory;

namespace RetailFlow.Domain.Catalog;

public sealed class Product
{
    private readonly List<ProductImage> _images = [];

    private Product()
    {
        Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public string? Sku { get; private set; }

    public decimal Price { get; private set; }

    public ProductStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public Category? Category { get; private set; }

    public InventoryItem? InventoryItem { get; private set; }

    public IReadOnlyCollection<ProductImage> Images => _images;

    public static Product Create(
        Guid id,
        Guid storeId,
        Guid categoryId,
        string name,
        decimal price,
        ProductStatus status,
        DateTimeOffset createdAtUtc,
        string? description = null,
        string? sku = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Product id cannot be empty.", nameof(id));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        }

        if (categoryId == Guid.Empty)
        {
            throw new ArgumentException("CategoryId cannot be empty.", nameof(categoryId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        if (price < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(price),
                "Product price cannot be negative.");
        }

        var normalizedSku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        return new Product
        {
            Id = id,
            StoreId = storeId,
            CategoryId = categoryId,
            Name = name.Trim(),
            Description = normalizedDescription,
            Sku = normalizedSku,
            Price = price,
            Status = status,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }
}
