using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentityScopeUnitTests.Infrastructure.Identity;

public class IdentityLookupScopeTests
{
    [Fact]
    public void ForBackOffice_CreatesGlobalScope()
    {
        var scope = IdentityLookupScope.ForBackOffice();

        Assert.Equal(AccountKind.BackOffice, scope.AccountKind);
        Assert.Null(scope.StoreId);
    }

    [Fact]
    public void ForStoreCustomer_WithValidStoreId_CreatesStoreScopedScope()
    {
        var storeId = Guid.NewGuid();

        var scope = IdentityLookupScope.ForStoreCustomer(storeId);

        Assert.Equal(AccountKind.StoreCustomer, scope.AccountKind);
        Assert.Equal(storeId, scope.StoreId);
    }

    [Fact]
    public void ForStoreCustomer_WithEmptyStoreId_ThrowsArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => IdentityLookupScope.ForStoreCustomer(Guid.Empty));

        Assert.Equal("storeId", exception.ParamName);
    }
}