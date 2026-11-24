using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class ScopedUserStoreLookupTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task CustomerLookups_AreRestrictedToCurrentStore_AndBackOfficeRemainsGlobal()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new IdentitySpikeDbContext(options);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var storeA = CreateStore("lookup-store-a");
        var storeB = CreateStore("lookup-store-b");

        const string sharedEmail = "shared-customer@spike.invalid";
        const string normalizedSharedEmail = "SHARED-CUSTOMER@SPIKE.INVALID";

        var userA = CreateCustomer(
            storeA.Id,
            "store-a-customer",
            sharedEmail,
            normalizedSharedEmail);

        var userB = CreateCustomer(
            storeB.Id,
            "store-b-customer",
            sharedEmail,
            normalizedSharedEmail);

        var backOfficeUser = CreateBackOfficeUser();

        db.Stores.AddRange(storeA, storeB);
        db.Users.AddRange(userA, userB, backOfficeUser);
        await db.SaveChangesAsync();

        // Store A scope: all supported lookup paths must remain tenant-scoped.
        var storeAAccessor = new IdentityLookupScopeAccessor();
        storeAAccessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(storeA.Id));

        using (var storeAUserStore = new ScopedUserStore(db, storeAAccessor))
        {
            var users = await storeAUserStore.Users.ToListAsync();

            Assert.Single(users);
            Assert.Equal(userA.Id, users[0].Id);

            Assert.Equal(
                userA.Id,
                (await storeAUserStore.FindByIdAsync(userA.Id))?.Id);

            // Same email exists in Store B, but the lookup returns only Store A's user.
            Assert.Equal(
                userA.Id,
                (await storeAUserStore.FindByEmailAsync(normalizedSharedEmail))?.Id);

            Assert.Equal(
                userA.Id,
                (await storeAUserStore.FindByNameAsync(userA.NormalizedUserName!))?.Id);

            // A foreign user's ID must not disclose that the account exists.
            Assert.Null(await storeAUserStore.FindByIdAsync(userB.Id));

            // A foreign user's internal username must not be discoverable.
            Assert.Null(
                await storeAUserStore.FindByNameAsync(userB.NormalizedUserName!));
        }

        // BackOffice scope is global and must not be accidentally restricted to a store.
        var backOfficeAccessor = new IdentityLookupScopeAccessor();
        backOfficeAccessor.SetCurrent(IdentityLookupScope.ForBackOffice());

        using (var backOfficeUserStore = new ScopedUserStore(db, backOfficeAccessor))
        {
            var users = await backOfficeUserStore.Users.ToListAsync();

            Assert.Single(users);
            Assert.Equal(backOfficeUser.Id, users[0].Id);

            Assert.Equal(
                backOfficeUser.Id,
                (await backOfficeUserStore.FindByEmailAsync(
                    backOfficeUser.NormalizedEmail!))?.Id);

            Assert.Null(await backOfficeUserStore.FindByIdAsync(userA.Id));
            Assert.Null(await backOfficeUserStore.FindByIdAsync(userB.Id));
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public void Users_WithoutIdentityLookupScope_FailClosed()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        using var db = new IdentitySpikeDbContext(options);
        using var userStore = new ScopedUserStore(
            db,
            new IdentityLookupScopeAccessor());

        Assert.Throws<InvalidOperationException>(
            () => _ = userStore.Users);
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
        string prefix,
        string email,
        string normalizedEmail)
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var userName = $"{prefix}-{uniqueId}";

        return new ApplicationUser
        {
            Id = uniqueId,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
    }

    private static ApplicationUser CreateBackOfficeUser()
    {
        var uniqueId = Guid.NewGuid().ToString("N");
        var email = $"backoffice-{uniqueId}@spike.invalid";

        return new ApplicationUser
        {
            Id = uniqueId,
            AccountKind = AccountKind.BackOffice,
            StoreId = null,
            UserName = $"backoffice-{uniqueId}",
            NormalizedUserName = $"BACKOFFICE-{uniqueId}".ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
    }
}
