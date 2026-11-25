using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Stores;

namespace RetailFlow.Domain.Fulfillment;

/// <summary>
/// Store-scoped delivery zone with a flat MVP delivery fee (FR-038, FR-039).
/// </summary>
public sealed class DeliveryZone
{
    private DeliveryZone()
    {
        Name = string.Empty;
        NormalizedName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid StoreId { get; private set; }

    public string Name { get; private set; }

    public string NormalizedName { get; private set; }

    public decimal FlatFee { get; private set; }

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static DeliveryZone Create(
        Guid id,
        Guid storeId,
        string name,
        decimal flatFee,
        StoreSettings storeSettings,
        Currency currency,
        DateTimeOffset createdAtUtc,
        bool isEnabled = true)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("DeliveryZone id cannot be empty.", nameof(id));
        }

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Delivery zone name is required.", nameof(name));
        }

        ValidateFee(storeId, flatFee, storeSettings, currency);

        var trimmedName = name.Trim();
        return new DeliveryZone
        {
            Id = id,
            StoreId = storeId,
            Name = trimmedName,
            NormalizedName = trimmedName.ToUpperInvariant(),
            FlatFee = flatFee,
            IsEnabled = isEnabled,
            CreatedAtUtc = createdAtUtc.ToUniversalTime()
        };
    }

    public void UpdateFee(
        decimal flatFee,
        StoreSettings storeSettings,
        Currency currency)
    {
        ValidateFee(StoreId, flatFee, storeSettings, currency);
        FlatFee = flatFee;
    }

    public void SetEnabled(bool isEnabled) => IsEnabled = isEnabled;

    private static void ValidateFee(
        Guid storeId,
        decimal flatFee,
        StoreSettings storeSettings,
        Currency currency)
    {
        ArgumentNullException.ThrowIfNull(storeSettings);
        ArgumentNullException.ThrowIfNull(currency);

        if (storeSettings.StoreId != storeId)
        {
            throw new ArgumentException(
                "StoreSettings does not belong to the delivery zone's store.",
                nameof(storeSettings));
        }

        if (!string.Equals(storeSettings.CurrencyCode, currency.Code, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Currency '{currency.Code}' does not match store settings currency '{storeSettings.CurrencyCode}'.",
                nameof(currency));
        }

        if (flatFee < 0m)
        {
            throw new ArgumentOutOfRangeException(
                nameof(flatFee),
                "Delivery zone flat fee cannot be negative.");
        }

        MoneyPrecision.EnsureValidAmount(flatFee, currency.MinorUnit, nameof(flatFee));
    }
}
