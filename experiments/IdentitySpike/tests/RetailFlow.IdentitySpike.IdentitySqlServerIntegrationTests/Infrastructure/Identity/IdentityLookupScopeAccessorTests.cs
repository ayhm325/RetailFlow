using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentityLookupScopeAccessorTests
{
    [Fact]
    public void SetCurrent_CalledTwice_ThrowsAndPreservesOriginalScope()
    {
        // Arrange
        var accessor = new IdentityLookupScopeAccessor();

        var firstScope = IdentityLookupScope.ForStoreCustomer(Guid.NewGuid());
        var secondScope = IdentityLookupScope.ForStoreCustomer(Guid.NewGuid());

        // Act
        accessor.SetCurrent(firstScope);

        var exception = Assert.Throws<InvalidOperationException>(
            () => accessor.SetCurrent(secondScope));

        // Assert
        Assert.Equal(
            "Identity lookup scope has already been established.",
            exception.Message);

        Assert.Same(firstScope, accessor.Current);
        Assert.NotSame(secondScope, accessor.Current);
    }
}
