using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

public sealed class IdentityScopeResolver : IIdentityScopeResolver
{
    private readonly IdentitySpikeDbContext _dbContext;

    public IdentityScopeResolver(IdentitySpikeDbContext dbContext)
    {
        _dbContext = dbContext
            ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<Guid?> ResolveActiveStoreIdAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        cancellationToken.ThrowIfCancellationRequested();

        // Normalize the incoming route value.
        // Store creation must persist slugs in canonical lowercase form.
        var normalizedSlug = slug.ToLowerInvariant();

        return await _dbContext.Stores
            .AsNoTracking()
            .Where(store =>
                store.Slug == normalizedSlug &&
                store.IsActive)
            .Select(store => (Guid?)store.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
