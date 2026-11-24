using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class ScopedUserStoreLoginTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task LoginOperations_AreRestrictedToCurrentStore_AndFailClosedWithoutScope()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new IdentitySpikeDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var storeA = CreateStore("login-store-a");
        var storeB = CreateStore("login-store-b");

        var userA = CreateCustomer(storeA.Id, "customer-a");
        var userB = CreateCustomer(storeB.Id, "customer-b");

        const string provider = "TestExternalProvider";
        const string providerKeyA = "external-account-a";
        const string providerKeyB = "external-account-b";

        db.Stores.AddRange(storeA, storeB);
        db.Users.AddRange(userA, userB);

        // Seed a login that belongs to Store B. The current request will be
        // scoped to Store A and must not resolve Store B's user.
        db.Set<IdentityUserLogin<string>>().Add(
            new IdentityUserLogin<string>
            {
                LoginProvider = provider,
                ProviderKey = providerKeyB,
                ProviderDisplayName = "Test Provider",
                UserId = userB.Id
            });

        await db.SaveChangesAsync();

        var accessor = new IdentityLookupScopeAccessor();
        accessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(storeA.Id));

        using var userStore = new ScopedUserStore(db, accessor);
        IUserLoginStore<ApplicationUser> loginStore = userStore;

        var loginA = new UserLoginInfo(
            provider,
            providerKeyA,
            "Test Provider");

        // 1. Store A can add an external login to its own persisted user.
        await loginStore.AddLoginAsync(
            userA,
            loginA,
            CancellationToken.None);

        await db.SaveChangesAsync();

        // 2. Store A can read its own external login.
        var loginsA = await loginStore.GetLoginsAsync(
            userA,
            CancellationToken.None);

        var storedLoginA = Assert.Single(loginsA);
        Assert.Equal(provider, storedLoginA.LoginProvider);
        Assert.Equal(providerKeyA, storedLoginA.ProviderKey);

        // 3. FindByLogin resolves Store A's user.
        var foundUserA = await loginStore.FindByLoginAsync(
            provider,
            providerKeyA,
            CancellationToken.None);

        Assert.NotNull(foundUserA);
        Assert.Equal(userA.Id, foundUserA.Id);

        // 4. A login associated with Store B must not resolve a user in Store A.
        var foundUserBFromStoreA = await loginStore.FindByLoginAsync(
            provider,
            providerKeyB,
            CancellationToken.None);

        Assert.Null(foundUserBFromStoreA);

        // 5. A forged in-memory object cannot access Store B's persisted login
        // by copying Store A's scope values onto Store B's user ID.
        var forgedUserB = new ApplicationUser
        {
            Id = userB.Id,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeA.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loginStore.GetLoginsAsync(
                forgedUserB,
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loginStore.AddLoginAsync(
                forgedUserB,
                new UserLoginInfo(
                    provider,
                    "forged-external-account",
                    "Test Provider"),
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => loginStore.RemoveLoginAsync(
                forgedUserB,
                provider,
                providerKeyB,
                CancellationToken.None));

        // 6. Store A can remove its own login.
        await loginStore.RemoveLoginAsync(
            userA,
            provider,
            providerKeyA,
            CancellationToken.None);

        await db.SaveChangesAsync();

        Assert.Empty(
            await loginStore.GetLoginsAsync(
                userA,
                CancellationToken.None));

        // 7. No matching login must still fail closed if no Identity scope
        // has been established. This assertion requires the explicit
        // GetRequiredScope() guard in ScopedUserStore.FindByLoginAsync.
        using var noScopeStore = new ScopedUserStore(
            db,
            new IdentityLookupScopeAccessor());

        IUserLoginStore<ApplicationUser> noScopeLoginStore = noScopeStore;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => noScopeLoginStore.FindByLoginAsync(
                "UnknownProvider",
                "UnknownProviderKey",
                CancellationToken.None));

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
