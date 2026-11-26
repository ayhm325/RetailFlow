using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Stores;

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
        StoreSettings storeSettings,
        Currency currency,
        ProductStatus status,
        DateTimeOffset createdAtUtc,
        string? description = null,
        string? sku = null)
    {
        ArgumentNullException.ThrowIfNull(storeSettings);
        ArgumentNullException.ThrowIfNull(currency);

        if (storeSettings.StoreId != storeId)
        {
            throw new ArgumentException(
                "StoreSettings does not belong to the product's store.",
                nameof(storeSettings));
        }

        if (!string.Equals(storeSettings.CurrencyCode, currency.Code, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Currency '{currency.Code}' does not match store settings currency '{storeSettings.CurrencyCode}'.",
                nameof(currency));
        }

        MoneyPrecision.EnsureValidAmount(price, currency.MinorUnit, nameof(price));
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

    public void UpdateDetails(
        Guid categoryId,
        string name,
        decimal price,
        StoreSettings storeSettings,
        Currency currency,
        string? description = null,
        string? sku = null)
    {
        ArgumentNullException.ThrowIfNull(storeSettings);
        ArgumentNullException.ThrowIfNull(currency);

        if (storeSettings.StoreId != StoreId)
        {
            throw new ArgumentException(
                "StoreSettings does not belong to the product's store.",
                nameof(storeSettings));
        }

        if (!string.Equals(storeSettings.CurrencyCode, currency.Code, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Currency '{currency.Code}' does not match store settings currency '{storeSettings.CurrencyCode}'.",
                nameof(currency));
        }

        MoneyPrecision.EnsureValidAmount(price, currency.MinorUnit, nameof(price));

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

        CategoryId = categoryId;
        Name = name.Trim();
        Price = price;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Sku = string.IsNullOrWhiteSpace(sku) ? null : sku.Trim();
    }

    public void Publish() => Status = ProductStatus.Published;

    public void Archive() => Status = ProductStatus.Archived;

    public void MoveToDraft() => Status = ProductStatus.Draft;
}
