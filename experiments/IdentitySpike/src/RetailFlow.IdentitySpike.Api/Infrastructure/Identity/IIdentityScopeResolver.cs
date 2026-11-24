namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

public interface IIdentityScopeResolver
{
    Task<Guid?> ResolveActiveStoreIdAsync(
        string slug,
        CancellationToken cancellationToken);
}
