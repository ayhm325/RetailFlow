using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class ScopedUserStoreClaimsTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task ClaimsOperations_AreRestrictedToCurrentStore()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new IdentitySpikeDbContext(options);

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var storeA = CreateStore("claims-store-a");
        var storeB = CreateStore("claims-store-b");

        var userA = CreateCustomer(storeA.Id, "customer-a");
        var userB = CreateCustomer(storeB.Id, "customer-b");

        var storeBSecretClaim = new IdentityUserClaim<string>
        {
            UserId = userB.Id,
            ClaimType = "test:tenant",
            ClaimValue = "store-b-secret"
        };

        db.Stores.AddRange(storeA, storeB);
        db.Users.AddRange(userA, userB);
        db.Set<IdentityUserClaim<string>>()
            .Add(storeBSecretClaim);

        await db.SaveChangesAsync();

        var accessor = new IdentityLookupScopeAccessor();

        accessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(storeA.Id));

        using var userStore = new ScopedUserStore(db, accessor);

        // Store A must not read Store B claims.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => userStore.GetClaimsAsync(userB));

        // Store A must not add claims to Store B.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => userStore.AddClaimsAsync(
                userB,
                new[] { new Claim("test", "unauthorized") }));

        // Store A must not replace Store B claims.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => userStore.ReplaceClaimAsync(
                userB,
                new Claim("test:tenant", "store-b-secret"),
                new Claim("test:tenant", "hacked")));

        // Store A must not remove Store B claims.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => userStore.RemoveClaimsAsync(
                userB,
                new[] { new Claim("test:tenant", "store-b-secret") }));

        // Even a forged user object must be rejected.
        var forgedUser = new ApplicationUser
        {
            Id = userB.Id,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeA.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => userStore.GetClaimsAsync(forgedUser));

        // Store A can manage its own claims.
        var ownClaim = new Claim("test:tenant", "store-a-value");

        await userStore.AddClaimsAsync(userA, new[] { ownClaim });
        await db.SaveChangesAsync();

        var claims = await userStore.GetClaimsAsync(userA);

        Assert.Contains(
            claims,
            claim => claim.Type == "test:tenant"
                && claim.Value == "store-a-value");

        // Replace its own claim.
        var replacement = new Claim("test:tenant", "store-a-updated");

        await userStore.ReplaceClaimAsync(
            userA,
            ownClaim,
            replacement);

        await db.SaveChangesAsync();

        claims = await userStore.GetClaimsAsync(userA);

        Assert.Contains(
            claims,
            claim => claim.Value == "store-a-updated");

        Assert.DoesNotContain(
            claims,
            claim => claim.Value == "store-a-value");

        // Remove its own claim.
        await userStore.RemoveClaimsAsync(
            userA,
            new[] { replacement });

        await db.SaveChangesAsync();

        claims = await userStore.GetClaimsAsync(userA);

        Assert.DoesNotContain(
            claims,
            claim => claim.Type == "test:tenant");

        // Verify Store B claim was not modified.
        var persistedForeignClaim = await db
            .Set<IdentityUserClaim<string>>()
            .AsNoTracking()
            .SingleAsync(claim => claim.UserId == userB.Id);

        Assert.Equal("store-b-secret", persistedForeignClaim.ClaimValue);

        // Discard all test data.
        await transaction.RollbackAsync();
    }

    private static SpikeStore CreateStore(string prefix)
    {
        return new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = prefix,
            Slug = $"{prefix}-{Guid.NewGuid():N}",
            IsActive = true
        };
    }

    private static ApplicationUser CreateCustomer(
        Guid storeId,
        string prefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        var email = $"{uniqueId}@spike.invalid";

        return new ApplicationUser
        {
            Id = uniqueId,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId,
            UserName = $"{prefix}-{uniqueId}",
            NormalizedUserName = $"{prefix}-{uniqueId}".ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
    }
}