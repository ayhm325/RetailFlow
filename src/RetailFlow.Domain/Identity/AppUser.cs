namespace RetailFlow.Domain.Identity;

public enum AppRole
{
    Customer = 1,
    MerchantOwner = 2,
    PlatformAdmin = 3
}

public sealed class AppUser
{
    private AppUser()
    {
        Email = string.Empty;
        NormalizedEmail = string.Empty;
        DisplayName = string.Empty;
        PasswordHash = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid? StoreId { get; private set; }
    public string Email { get; private set; }
    public string NormalizedEmail { get; private set; }
    public string DisplayName { get; private set; }
    public string PasswordHash { get; private set; }
    public AppRole Role { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static AppUser CreateStoreUser(
        Guid id,
        Guid storeId,
        string email,
        string displayName,
        string passwordHash,
        AppRole role,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("User id cannot be empty.", nameof(id));
        if (storeId == Guid.Empty) throw new ArgumentException("StoreId is required for store users.", nameof(storeId));
        if (role == AppRole.PlatformAdmin) throw new ArgumentException("PlatformAdmin cannot be store-scoped.", nameof(role));

        return CreateInternal(id, storeId, email, displayName, passwordHash, role, createdAtUtc);
    }

    public static AppUser CreatePlatformAdmin(
        Guid id,
        string email,
        string displayName,
        string passwordHash,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("User id cannot be empty.", nameof(id));
        return CreateInternal(id, null, email, displayName, passwordHash, AppRole.PlatformAdmin, createdAtUtc);
    }

    private static AppUser CreateInternal(
        Guid id,
        Guid? storeId,
        string email,
        string displayName,
        string passwordHash,
        AppRole role,
        DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("DisplayName is required.", nameof(displayName));
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new ArgumentException("PasswordHash is required.", nameof(passwordHash));

        var trimmedEmail = email.Trim();
        return new AppUser
        {
            Id = id,
            StoreId = storeId,
            Email = trimmedEmail,
            NormalizedEmail = trimmedEmail.ToUpperInvariant(),
            DisplayName = displayName.Trim(),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Deactivate() => IsActive = false;
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
