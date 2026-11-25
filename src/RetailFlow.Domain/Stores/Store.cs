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

    public string? LogoUrl { get; private set; }

    public string? PrimaryColor { get; private set; }

    public string? ContactEmail { get; private set; }

    public string? ContactPhone { get; private set; }

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

    public void UpdateBranding(
        string name,
        string? logoUrl = null,
        string? primaryColor = null,
        string? contactEmail = null,
        string? contactPhone = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Store name is required.", nameof(name));
        }

        Name = name.Trim();
        LogoUrl = NormalizeOptional(logoUrl);
        PrimaryColor = NormalizeOptional(primaryColor);
        ContactEmail = NormalizeOptional(contactEmail);
        ContactPhone = NormalizeOptional(contactPhone);
    }

    public void Approve()
    {
        if (Status != StoreStatus.PendingApproval && Status != StoreStatus.Suspended)
        {
            throw new InvalidOperationException(
                $"Cannot approve or reactivate store from status '{Status}'.");
        }

        Status = StoreStatus.Active;
    }

    public void Reject()
    {
        if (Status != StoreStatus.PendingApproval)
        {
            throw new InvalidOperationException(
                $"Cannot reject store from status '{Status}'.");
        }

        Status = StoreStatus.Rejected;
    }

    public void Suspend()
    {
        if (Status != StoreStatus.Active)
        {
            throw new InvalidOperationException(
                $"Cannot suspend store from status '{Status}'.");
        }

        Status = StoreStatus.Suspended;
    }

    public static string NormalizeSlug(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Store slug is required.", nameof(slug));
        }

        return slug.Trim().ToLowerInvariant();
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
