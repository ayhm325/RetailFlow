namespace RetailFlow.Domain.Catalog;

public sealed class ProductImage
{
    public const int MaxImageUrlLength = 1000;
    public const int MaxAltTextLength = 250;

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

        var trimmedUrl = imageUrl.Trim();
        if (trimmedUrl.Length > MaxImageUrlLength)
        {
            throw new ArgumentException($"ImageUrl cannot exceed {MaxImageUrlLength} characters.", nameof(imageUrl));
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("ImageUrl must be an absolute HTTP or HTTPS URL.", nameof(imageUrl));
        }

        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "SortOrder cannot be negative.");
        }

        var normalizedAltText = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        if (normalizedAltText is { Length: > MaxAltTextLength })
        {
            throw new ArgumentException($"AltText cannot exceed {MaxAltTextLength} characters.", nameof(altText));
        }

        return new ProductImage
        {
            Id = id,
            StoreId = storeId,
            ProductId = productId,
            ImageUrl = trimmedUrl,
            AltText = normalizedAltText,
            SortOrder = sortOrder
        };
    }
}
