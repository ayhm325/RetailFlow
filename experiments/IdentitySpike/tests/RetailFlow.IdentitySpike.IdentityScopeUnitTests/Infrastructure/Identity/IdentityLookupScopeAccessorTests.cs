using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentityScopeUnitTests.Infrastructure.Identity;

public class IdentityLookupScopeAccessorTests
{
    [Fact]
    public void Current_IsNull_BeforeScopeIsAssigned()
    {
        var accessor = new IdentityLookupScopeAccessor();

        Assert.Null(accessor.Current);
    }

    [Fact]
    public void SetCurrent_StoresTheAssignedScope()
    {
        var accessor = new IdentityLookupScopeAccessor();
        var scope = IdentityLookupScope.ForBackOffice();

        accessor.SetCurrent(scope);

        Assert.Same(scope, accessor.Current);
    }

    [Fact]
    public void SetCurrent_CannotBeCalledTwice()
    {
        var accessor = new IdentityLookupScopeAccessor();

        var firstScope = IdentityLookupScope.ForBackOffice();

        var secondScope =
            IdentityLookupScope.ForStoreCustomer(Guid.NewGuid());

        accessor.SetCurrent(firstScope);

        var exception = Assert.Throws<InvalidOperationException>(
            () => accessor.SetCurrent(secondScope));

        Assert.Equal(
            "Identity lookup scope has already been established.",
            exception.Message);

        Assert.Same(firstScope, accessor.Current);
        Assert.NotSame(secondScope, accessor.Current);
    }

    [Fact]
    public void SetCurrent_WithNull_ThrowsArgumentNullException()
    {
        var accessor = new IdentityLookupScopeAccessor();

        Assert.Throws<ArgumentNullException>(
            () => accessor.SetCurrent(null!));
    }

    [Fact]
    public void DifferentAccessors_DoNotShareTheirScopes()
    {
        var firstAccessor = new IdentityLookupScopeAccessor();
        var secondAccessor = new IdentityLookupScopeAccessor();

        var scope = IdentityLookupScope.ForBackOffice();

        firstAccessor.SetCurrent(scope);

        Assert.Same(scope, firstAccessor.Current);
        Assert.Null(secondAccessor.Current);
    }
}