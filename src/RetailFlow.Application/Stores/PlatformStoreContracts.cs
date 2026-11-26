using RetailFlow.Domain.Stores;

namespace RetailFlow.Application.Stores;

public sealed record CreateStoreCommand(
    string Name,
    string Slug,
    StoreType StoreType,
    string CurrencyCode,
    string TimeZoneId,
    bool DeliveryEnabled,
    bool PickupEnabled,
    string? PublicAddress = null,
    string? FulfillmentAddress = null);

public sealed record PlatformStoreDto(
    Guid Id,
    string Name,
    string Slug,
    StoreType StoreType,
    StoreStatus Status,
    string CurrencyCode,
    DateTimeOffset CreatedAtUtc);

public interface IPlatformStoreService
{
    Task<PlatformStoreDto> CreateStoreAsync(
        CreateStoreCommand command,
        CancellationToken cancellationToken);

    Task<bool> ApproveStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken);

    Task<bool> RejectStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken);

    Task<bool> SuspendStoreAsync(
        Guid storeId,
        CancellationToken cancellationToken);
}
