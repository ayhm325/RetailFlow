using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Stores;
using RetailFlow.Domain.Fulfillment;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Stores;

public sealed class MerchantStoreService : IMerchantStoreService
{
    private readonly RetailFlowDbContext _dbContext;

    public MerchantStoreService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> UpdateBrandingAsync(
        Guid storeId,
        UpdateStoreBrandingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var store = await _dbContext.Stores
            .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

        if (store is null)
        {
            return false;
        }

        store.UpdateBranding(
            command.Name,
            command.LogoUrl,
            command.PrimaryColor,
            command.ContactEmail,
            command.ContactPhone);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> UpdateAddressesAsync(
        Guid storeId,
        UpdateStoreAddressesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var store = await _dbContext.Stores
            .Include(s => s.Settings)
            .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

        if (store?.Settings is null)
        {
            return false;
        }

        store.Settings.UpdateAddresses(
            store,
            command.PublicAddress,
            command.FulfillmentAddress);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<MerchantDeliveryZoneDto> CreateDeliveryZoneAsync(
        Guid storeId,
        CreateDeliveryZoneCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var settings = await _dbContext.StoreSettings
            .Include(ss => ss.Currency)
            .SingleOrDefaultAsync(ss => ss.StoreId == storeId, cancellationToken)
            ?? throw new InvalidOperationException($"StoreSettings not found for store '{storeId}'.");

        var currency = settings.Currency
            ?? throw new InvalidOperationException($"Currency '{settings.CurrencyCode}' not found.");

        var zone = DeliveryZone.Create(
            Guid.NewGuid(),
            storeId,
            command.Name,
            command.FlatFee,
            settings,
            currency,
            DateTimeOffset.UtcNow);

        if (!command.IsEnabled)
        {
            zone.SetEnabled(false);
        }

        _dbContext.DeliveryZones.Add(zone);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(zone);
    }

    public async Task<MerchantDeliveryZoneDto?> UpdateDeliveryZoneAsync(
        Guid storeId,
        Guid zoneId,
        UpdateDeliveryZoneCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var zone = await _dbContext.DeliveryZones
            .SingleOrDefaultAsync(z => z.Id == zoneId && z.StoreId == storeId, cancellationToken);

        if (zone is null)
        {
            return null;
        }

        var settings = await _dbContext.StoreSettings
            .Include(ss => ss.Currency)
            .SingleOrDefaultAsync(ss => ss.StoreId == storeId, cancellationToken)
            ?? throw new InvalidOperationException($"StoreSettings not found for store '{storeId}'.");

        var currency = settings.Currency
            ?? throw new InvalidOperationException($"Currency '{settings.CurrencyCode}' not found.");

        zone.UpdateFee(command.FlatFee, settings, currency);
        zone.SetEnabled(command.IsEnabled);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(zone);
    }

    private static MerchantDeliveryZoneDto Map(DeliveryZone zone) =>
        new(zone.Id, zone.StoreId, zone.Name, zone.FlatFee, zone.IsEnabled, zone.CreatedAtUtc);
}
