namespace RetailFlow.Domain.Identity;

public enum AppRole
{
    Customer = 1,
    MerchantOwner = 2,
    PlatformAdmin = 3
}

public static class IdentityRoleNames
{
    public const string Customer = nameof(AppRole.Customer);
    public const string MerchantOwner = nameof(AppRole.MerchantOwner);
    public const string PlatformAdmin = nameof(AppRole.PlatformAdmin);

    public static string FromAppRole(AppRole role) => role switch
    {
        AppRole.Customer => Customer,
        AppRole.MerchantOwner => MerchantOwner,
        AppRole.PlatformAdmin => PlatformAdmin,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported role.")
    };
}

public sealed class SessionTicket
{
    private SessionTicket()
    {
        Key = string.Empty;
        Value = [];
    }

    public string Key { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? StoreId { get; private set; }
    public byte[] Value { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset LastSeenAtUtc { get; private set; }

    public static SessionTicket Create(
        string key,
        Guid userId,
        Guid? storeId,
        byte[] value,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset lastSeenAtUtc)
    {
        return new SessionTicket
        {
            Key = key,
            UserId = userId,
            StoreId = storeId,
            Value = value,
            ExpiresAtUtc = expiresAtUtc,
            LastSeenAtUtc = lastSeenAtUtc
        };
    }

    public void Renew(byte[] value, DateTimeOffset expiresAtUtc, DateTimeOffset lastSeenAtUtc)
    {
        Value = value;
        ExpiresAtUtc = expiresAtUtc;
        LastSeenAtUtc = lastSeenAtUtc;
    }
}
