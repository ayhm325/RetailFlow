namespace RetailFlow.Domain.Catalog;

public sealed class ProductImage
{
    private ProductImage()
    {
        ImageUrl = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public Guid ProductId { get; private set; }

    public string ImageUrl { get; private set; }

    public string? AltText { get; private set; }

    public int SortOrder { get; private set; }

    public static ProductImage Create(
        Guid id,
        Guid storeId,
        Guid productId,
        string imageUrl,
        int sortOrder = 0,
        string? altText = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Image id cannot be empty.", nameof(id));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        }

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId cannot be empty.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException("ImageUrl is required.", nameof(imageUrl));
        }

        return new ProductImage
        {
            Id = id,
            StoreId = storeId,
            ProductId = productId,
            ImageUrl = imageUrl.Trim(),
            AltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim(),
            SortOrder = sortOrder
        };
    }
}
