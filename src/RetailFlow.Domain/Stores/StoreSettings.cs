using RetailFlow.Domain.Currencies;

namespace RetailFlow.Domain.Stores;

/// <summary>
/// Tenant-owned store configuration (one-to-one with Store in MVP).
/// </summary>
public sealed class StoreSettings
{
    private StoreSettings()
    {
        CurrencyCode = string.Empty;
        TimeZoneId = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string CurrencyCode { get; private set; }

    public string TimeZoneId { get; private set; }

    public bool DeliveryEnabled { get; private set; }

    public bool PickupEnabled { get; private set; }

    public Currency? Currency { get; private set; }

    public static StoreSettings Create(
        Guid id,
        Store store,
        Currency currency,
        string timeZoneId,
        bool deliveryEnabled,
        bool pickupEnabled)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("StoreSettings id cannot be empty.", nameof(id));
        }

        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(currency);

        if (!currency.IsActive)
        {
            throw new InvalidOperationException(
                $"Currency '{currency.Code}' is inactive and cannot be assigned to store settings.");
        }

        if (store.StoreType == StoreType.OnlineOnly && pickupEnabled)
        {
            throw new InvalidOperationException(
                "Pickup cannot be enabled for OnlineOnly stores.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("TimeZoneId is required.", nameof(timeZoneId));
        }

        return new StoreSettings
        {
            Id = id,
            StoreId = store.Id,
            CurrencyCode = currency.Code,
            TimeZoneId = timeZoneId.Trim(),
            DeliveryEnabled = deliveryEnabled,
            PickupEnabled = pickupEnabled
        };
    }

    public void UpdateFulfillmentOptions(
        Store store,
        bool deliveryEnabled,
        bool pickupEnabled)
    {
        ArgumentNullException.ThrowIfNull(store);

        if (store.Id != StoreId)
        {
            throw new ArgumentException(
                "Store does not match these store settings.",
                nameof(store));
        }

        if (store.StoreType == StoreType.OnlineOnly && pickupEnabled)
        {
            throw new InvalidOperationException(
                "Pickup cannot be enabled for OnlineOnly stores.");
        }

        DeliveryEnabled = deliveryEnabled;
        PickupEnabled = pickupEnabled;
    }
}
