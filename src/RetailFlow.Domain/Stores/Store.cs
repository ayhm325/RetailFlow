namespace RetailFlow.Domain.Stores;

public sealed class Store
{
    private Store()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public StoreType StoreType { get; private set; }

    public StoreStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public StoreSettings? Settings { get; private set; }

    public bool IsPubliclyAccessible => Status == StoreStatus.Active;

    public static Store Create(
        Guid id,
        string name,
        string slug,
        StoreType storeType,
        StoreStatus status,
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

        return new Store
        {
            Id = id,
            Name = name.Trim(),
            Slug = normalizedSlug,
            StoreType = storeType,
            Status = status,
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
}
