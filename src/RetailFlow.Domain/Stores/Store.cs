namespace RetailFlow.Domain.Stores;

public sealed class Store
{
    private Store()
    {
        Name = string.Empty;
        Slug = string.Empty;
        CurrencyCode = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public StoreType StoreType { get; private set; }

    public StoreStatus Status { get; private set; }

    public string CurrencyCode { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public bool IsPubliclyAccessible => Status == StoreStatus.Active;

    public static Store Create(
        Guid id,
        string name,
        string slug,
        StoreType storeType,
        StoreStatus status,
        string currencyCode,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Store id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Store name is required.", nameof(name));
        }

        var normalizedSlug = NormalizeSlug(slug);
        var normalizedCurrency = NormalizeCurrencyCode(currencyCode);

        return new Store
        {
            Id = id,
            Name = name.Trim(),
            Slug = normalizedSlug,
            StoreType = storeType,
            Status = status,
            CurrencyCode = normalizedCurrency,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Store slug is required.", nameof(slug));
        }

        return slug.Trim().ToLowerInvariant();
    }

    public static string NormalizeCurrencyCode(string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
        {
            throw new ArgumentException("Currency code is required.", nameof(currencyCode));
        }

        var trimmed = currencyCode.Trim().ToUpperInvariant();

        if (trimmed.Length != 3 || !trimmed.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                "Currency code must be a 3-letter ISO 4217 code.",
                nameof(currencyCode));
        }

        return trimmed;
    }
}
