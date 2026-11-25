using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Stores;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Stores;

public sealed class PlatformStoreService : IPlatformStoreService
{
    private readonly RetailFlowDbContext _dbContext;

    public PlatformStoreService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PlatformStoreDto> CreateStoreAsync(
        CreateStoreCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedCurrencyCode = Currency.NormalizeCode(command.CurrencyCode);
        var currency = await _dbContext.Currencies
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.Code == normalizedCurrencyCode, cancellationToken)
            ?? throw new InvalidOperationException($"Currency '{normalizedCurrencyCode}' does not exist.");

        var store = Store.Create(
            Guid.NewGuid(),
            command.Name,
            command.Slug,
            command.StoreType,
            command.InitialStatus,
            DateTimeOffset.UtcNow);

        var settings = StoreSettings.Create(
            Guid.NewGuid(),
            store,
            currency,
            command.TimeZoneId,
            command.DeliveryEnabled,
            command.PickupEnabled,
            command.PublicAddress,
            command.FulfillmentAddress);

        _dbContext.Stores.Add(store);
        _dbContext.StoreSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new PlatformStoreDto(
            store.Id,
            store.Name,
            store.Slug,
            store.StoreType,
            store.Status,
            settings.CurrencyCode,
            store.CreatedAtUtc);
    }

    public async Task<bool> ApproveStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

        if (store is null)
        {
            return false;
        }

        store.Approve();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RejectStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

        if (store is null)
        {
            return false;
        }

        store.Reject();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SuspendStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken)
    {
        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

        if (store is null)
        {
            return false;
        }

        store.Suspend();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
