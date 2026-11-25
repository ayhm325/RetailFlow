using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Stores;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Stores;

public sealed class StoreDirectoryResolver : IStoreDirectoryResolver
{
    private readonly RetailFlowDbContext _dbContext;

    public StoreDirectoryResolver(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StoreRequestContext?> ResolveActiveStoreAsync(
        string slug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var normalizedSlug = slug.Trim().ToLowerInvariant();

        return await _dbContext.Stores
            .AsNoTracking()
            .Where(s => s.Slug == normalizedSlug && s.Status == StoreStatus.Active)
            .Select(s => new StoreRequestContext(
                s.Id,
                s.Name,
                s.Slug,
                s.StoreType,
                s.CurrencyCode))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
