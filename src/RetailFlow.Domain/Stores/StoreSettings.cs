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
        Guid storeId,
        Currency currency,
        string timeZoneId,
        bool deliveryEnabled,
        bool pickupEnabled)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("StoreSettings id cannot be empty.", nameof(id));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        }

        ArgumentNullException.ThrowIfNull(currency);

        if (!currency.IsActive)
        {
            throw new InvalidOperationException(
                $"Currency '{currency.Code}' is inactive and cannot be assigned to store settings.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("TimeZoneId is required.", nameof(timeZoneId));
        }

        return new StoreSettings
        {
            Id = id,
            StoreId = storeId,
            CurrencyCode = currency.Code,
            TimeZoneId = timeZoneId.Trim(),
            DeliveryEnabled = deliveryEnabled,
            PickupEnabled = pickupEnabled
        };
    }
}
