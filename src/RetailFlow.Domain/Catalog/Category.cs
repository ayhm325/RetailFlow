namespace RetailFlow.Domain.Catalog;

public sealed class Category
{
    private Category()
    {
        Name = string.Empty;
        Slug = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Name { get; private set; }

    public string Slug { get; private set; }

    public bool IsActive { get; private set; }

    public int SortOrder { get; private set; }

    public static Category Create(
        Guid id,
        Guid storeId,
        string name,
        string slug,
        bool isActive = true,
        int sortOrder = 0)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Category id cannot be empty.", nameof(id));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Category slug is required.", nameof(slug));
        }

        return new Category
        {
            Id = id,
            StoreId = storeId,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            IsActive = isActive,
            SortOrder = sortOrder
        };
    }

    public void Update(
        string name,
        string slug,
        bool isActive,
        int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Category name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Category slug is required.", nameof(slug));
        }

        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        IsActive = isActive;
        SortOrder = sortOrder;
    }
}
