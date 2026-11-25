using RetailFlow.Domain.Stores;

namespace RetailFlow.Application.Stores;

public sealed record StoreRequestContext(
    Guid StoreId,
    string StoreName,
    string StoreSlug,
    StoreType StoreType,
    string CurrencyCode);
