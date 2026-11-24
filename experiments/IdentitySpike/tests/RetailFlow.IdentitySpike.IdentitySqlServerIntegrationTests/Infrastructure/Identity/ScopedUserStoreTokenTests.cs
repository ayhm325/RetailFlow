using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class ScopedUserStoreTokenTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task TokenOperations_AreRestrictedToCurrentStore()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new IdentitySpikeDbContext(options);

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var storeA = CreateStore("token-store-a");
        var storeB = CreateStore("token-store-b");

        var userA = CreateCustomer(storeA.Id, "customer-a");
        var userB = CreateCustomer(storeB.Id, "customer-b");

        const string provider = "RetailFlow.Spike";
        const string tokenName = "TestToken";

        // Create an existing token belonging to Store B.
        var storeBToken = new IdentityUserToken<string>
        {
            UserId = userB.Id,
            LoginProvider = provider,
            Name = tokenName,
            Value = "store-b-secret"
        };

        db.Stores.AddRange(storeA, storeB);
        db.Users.AddRange(userA, userB);
        db.Set<IdentityUserToken<string>>().Add(storeBToken);

        await db.SaveChangesAsync();

        var accessor = new IdentityLookupScopeAccessor();

        accessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(storeA.Id));

        using var userStore = new ScopedUserStore(db, accessor);

        // Use the actual ASP.NET Core Identity token store interface.
        IUserAuthenticationTokenStore<ApplicationUser> tokenStore =
            userStore;

        // Store A cannot read Store B token.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenStore.GetTokenAsync(
                userB, provider, tokenName, CancellationToken.None));

        // Store A cannot create or modify Store B token.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenStore.SetTokenAsync(
                userB,
                provider,
                tokenName,
                "unauthorized-value",
                CancellationToken.None));

        // Store A cannot delete Store B token.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenStore.RemoveTokenAsync(
                userB, provider, tokenName, CancellationToken.None));

        // A forged in-memory user must also be rejected.
        var forgedUser = new ApplicationUser
        {
            Id = userB.Id,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeA.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => tokenStore.GetTokenAsync(
                forgedUser, provider, tokenName, CancellationToken.None));

        // Store A can create its own token.
        await tokenStore.SetTokenAsync(
            userA,
            provider,
            tokenName,
            "store-a-token",
            CancellationToken.None);

        await db.SaveChangesAsync();

        var ownToken = await tokenStore.GetTokenAsync(
            userA, provider, tokenName, CancellationToken.None);

        Assert.Equal("store-a-token", ownToken);

        // Store A can update its own token.
        await tokenStore.SetTokenAsync(
            userA,
            provider,
            tokenName,
            "store-a-updated",
            CancellationToken.None);

        await db.SaveChangesAsync();

        ownToken = await tokenStore.GetTokenAsync(
            userA, provider, tokenName, CancellationToken.None);

        Assert.Equal("store-a-updated", ownToken);

        // Store A can delete its own token.
        await tokenStore.RemoveTokenAsync(
            userA, provider, tokenName, CancellationToken.None);

        await db.SaveChangesAsync();

        ownToken = await tokenStore.GetTokenAsync(
            userA, provider, tokenName, CancellationToken.None);

        Assert.Null(ownToken);

        // Verify Store B token remains unchanged.
        db.ChangeTracker.Clear();

        var persistedForeignToken = await db
            .Set<IdentityUserToken<string>>()
            .AsNoTracking()
            .SingleAsync(token =>
                token.UserId == userB.Id &&
                token.LoginProvider == provider &&
                token.Name == tokenName);

        Assert.Equal("store-b-secret", persistedForeignToken.Value);

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
        var userName = $"{prefix}-{uniqueId}";

        return new ApplicationUser
        {
            Id = uniqueId,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
    }
}