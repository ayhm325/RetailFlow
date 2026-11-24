using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

public sealed class IdentityLookupScope
{
    public AccountKind AccountKind { get; }

    public Guid? StoreId { get; }

    private IdentityLookupScope(
        AccountKind accountKind,
        Guid? storeId)
    {
        AccountKind = accountKind;
        StoreId = storeId;
    }

    public static IdentityLookupScope ForBackOffice()
    {
        return new IdentityLookupScope(
            AccountKind.BackOffice,
            null);
    }

    public static IdentityLookupScope ForStoreCustomer(
        Guid storeId)
    {
        if (storeId == Guid.Empty)
        {
            throw new ArgumentException(
                "Store ID cannot be empty.",
                nameof(storeId));
        }

        return new IdentityLookupScope(
            AccountKind.StoreCustomer,
            storeId);
    }
}