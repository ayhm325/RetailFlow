namespace RetailFlow.Infrastructure.Identity;

public enum IdentityScopeType
{
    Store = 1,
    Platform = 2,
    SystemProvisioning = 3
}

public sealed record IdentityLookupScope
{
    private IdentityLookupScope(IdentityScopeType scopeType, Guid? storeId)
    {
        ScopeType = scopeType;
        StoreId = storeId;
    }

    public IdentityScopeType ScopeType { get; }

    public Guid? StoreId { get; }

    public static IdentityLookupScope ForStore(Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty for Store scope.", nameof(storeId));
        }

        return new IdentityLookupScope(IdentityScopeType.Store, storeId);
    }

    public static IdentityLookupScope ForPlatform() =>
        new(IdentityScopeType.Platform, null);

    public static IdentityLookupScope ForSystemProvisioning() =>
        new(IdentityScopeType.SystemProvisioning, null);
}

public interface IIdentityLookupScopeAccessor
{
    IdentityLookupScope? Current { get; }

    void SetCurrent(IdentityLookupScope scope);

    void Clear();
}

public sealed class IdentityLookupScopeAccessor : IIdentityLookupScopeAccessor
{
    public IdentityLookupScope? Current { get; private set; }

    public void SetCurrent(IdentityLookupScope scope)
    {
        Current = scope ?? throw new ArgumentNullException(nameof(scope));
    }

    public void Clear()
    {
        Current = null;
    }
}

public sealed record IdentityScopeMetadata(IdentityScopeType ScopeType);
