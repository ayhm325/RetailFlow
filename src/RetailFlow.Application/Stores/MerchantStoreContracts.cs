namespace RetailFlow.Application.Stores;

public sealed record UpdateStoreBrandingCommand(
    string Name,
    string? LogoUrl,
    string? PrimaryColor,
    string? ContactEmail,
    string? ContactPhone);

public sealed record UpdateStoreAddressesCommand(
    string? PublicAddress,
    string? FulfillmentAddress);

public sealed record CreateDeliveryZoneCommand(
    string Name,
    decimal FlatFee,
    bool IsEnabled = true);

public sealed record UpdateDeliveryZoneCommand(
    decimal FlatFee,
    bool IsEnabled);

public sealed record MerchantDeliveryZoneDto(
    Guid Id,
    Guid StoreId,
    string Name,
    decimal FlatFee,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc);

public interface IMerchantStoreService
{
    Task<bool> UpdateBrandingAsync(
        Guid storeId,
        UpdateStoreBrandingCommand command,
        CancellationToken cancellationToken);

    Task<bool> UpdateAddressesAsync(
        Guid storeId,
        UpdateStoreAddressesCommand command,
        CancellationToken cancellationToken);

    Task<MerchantDeliveryZoneDto> CreateDeliveryZoneAsync(
        Guid storeId,
        CreateDeliveryZoneCommand command,
        CancellationToken cancellationToken);

    Task<MerchantDeliveryZoneDto?> UpdateDeliveryZoneAsync(
        Guid storeId,
        Guid zoneId,
        UpdateDeliveryZoneCommand command,
        CancellationToken cancellationToken);
}
