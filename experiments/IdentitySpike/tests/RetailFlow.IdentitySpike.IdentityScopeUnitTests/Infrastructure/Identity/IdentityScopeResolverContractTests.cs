using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentityScopeUnitTests.Infrastructure.Identity;

public sealed class IdentityScopeResolverContractTests
{
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task ResolveActiveStoreIdAsync_RejectsInvalidSlug(
        string slug)
    {
        using var dbContext = CreateDbContext();
        var resolver = new IdentityScopeResolver(dbContext);

        await Assert.ThrowsAsync<ArgumentException>(
            () => resolver.ResolveActiveStoreIdAsync(
                slug,
                CancellationToken.None));
    }

    [Fact]
    public async Task ResolveActiveStoreIdAsync_ThrowsWhenCancellationIsRequested()
    {
        using var dbContext = CreateDbContext();
        using var cancellationTokenSource = new CancellationTokenSource();

        var resolver = new IdentityScopeResolver(dbContext);

        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => resolver.ResolveActiveStoreIdAsync(
                "valid-store",
                cancellationTokenSource.Token));
    }

    private static IdentitySpikeDbContext CreateDbContext()
    {
        var options =
            new DbContextOptionsBuilder<IdentitySpikeDbContext>()
                .Options;

        return new IdentitySpikeDbContext(options);
    }
}
