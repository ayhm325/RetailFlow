using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

public sealed class PersistedAuthenticationTicket
{
    public string Key { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public AccountKind AccountKind { get; set; }

    public Guid StoreId { get; set; }

    public byte[] ProtectedTicket { get; set; } = [];

    public DateTimeOffset CreatedUtc { get; set; }

    public DateTimeOffset LastRenewedUtc { get; set; }

    public DateTimeOffset ExpiresUtc { get; set; }
}
