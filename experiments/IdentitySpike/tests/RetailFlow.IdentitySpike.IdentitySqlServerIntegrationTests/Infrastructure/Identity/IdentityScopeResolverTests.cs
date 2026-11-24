using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentityScopeResolverTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task ResolveActiveStoreIdAsync_ReturnsOnlyActiveKnownStore()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        var dbOptions =
            new DbContextOptionsBuilder<IdentitySpikeDbContext>()
                .UseSqlServer(connection)
                .Options;

        await using var db = new IdentitySpikeDbContext(dbOptions);

        await db.Database.UseTransactionAsync(transaction);

        var activeStore = CreateStore(isActive: true);
        var inactiveStore = CreateStore(isActive: false);

        db.Stores.AddRange(activeStore, inactiveStore);

        await db.SaveChangesAsync();

        var resolver = new IdentityScopeResolver(db);

        // An active store must resolve to its actual database Id.
        var activeStoreId =
            await resolver.ResolveActiveStoreIdAsync(
                activeStore.Slug.ToUpperInvariant(),
                CancellationToken.None);

        Assert.Equal(activeStore.Id, activeStoreId);

        // An inactive store must not produce a valid identity scope.
        var inactiveStoreId =
            await resolver.ResolveActiveStoreIdAsync(
                inactiveStore.Slug,
                CancellationToken.None);

        Assert.Null(inactiveStoreId);

        // An unknown slug must not produce a valid identity scope.
        var unknownStoreId =
            await resolver.ResolveActiveStoreIdAsync(
                $"unknown-{Guid.NewGuid():N}",
                CancellationToken.None);

        Assert.Null(unknownStoreId);

        await transaction.RollbackAsync();
    }

    private static SpikeStore CreateStore(bool isActive)
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        return new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = $"resolver-store-{uniqueId}",
            Slug = $"resolver-{uniqueId}",
            IsActive = isActive
        };
    }
}
