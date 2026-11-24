using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class ScopedUserStoreSecurityStateTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task LockoutAndSecurityStamp_AreRestrictedToCurrentStore()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new IdentitySpikeDbContext(options);

        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var storeA = CreateStore("security-store-a");
        var storeB = CreateStore("security-store-b");

        var userA = CreateCustomer(storeA.Id, "customer-a");
        var userB = CreateCustomer(storeB.Id, "customer-b");

        var originalStoreBStamp = userB.SecurityStamp;

        db.Stores.AddRange(storeA, storeB);
        db.Users.AddRange(userA, userB);

        await db.SaveChangesAsync();

        var accessor = new IdentityLookupScopeAccessor();

        accessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(storeA.Id));

        using var userStore = new ScopedUserStore(db, accessor);

        IUserLockoutStore<ApplicationUser> lockoutStore = userStore;

        IUserSecurityStampStore<ApplicationUser> securityStampStore =
            userStore;

        // -----------------------------------------
        // 1. Reject Lockout operations on Store B.
        // -----------------------------------------

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.GetLockoutEndDateAsync(
                userB, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.SetLockoutEndDateAsync(
                userB,
                DateTimeOffset.UtcNow.AddMinutes(15),
                CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.GetAccessFailedCountAsync(
                userB, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.IncrementAccessFailedCountAsync(
                userB, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.ResetAccessFailedCountAsync(
                userB, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.GetLockoutEnabledAsync(
                userB, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.SetLockoutEnabledAsync(
                userB,
                false,
                CancellationToken.None));

        // -----------------------------------------
        // 2. Reject Security Stamp operations on B.
        // -----------------------------------------

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => securityStampStore.GetSecurityStampAsync(
                userB, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => securityStampStore.SetSecurityStampAsync(
                userB,
                "unauthorized-stamp",
                CancellationToken.None));

        // -----------------------------------------
        // 3. Reject a forged in-memory user.
        // -----------------------------------------

        var forgedUser = new ApplicationUser
        {
            Id = userB.Id,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeA.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lockoutStore.GetAccessFailedCountAsync(
                forgedUser, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => securityStampStore.GetSecurityStampAsync(
                forgedUser, CancellationToken.None));

        // -----------------------------------------
        // 4. Allow Store A to manage its own Lockout.
        // -----------------------------------------

        Assert.Equal(
            0,
            await lockoutStore.GetAccessFailedCountAsync(
                userA, CancellationToken.None));

        Assert.True(
            await lockoutStore.GetLockoutEnabledAsync(
                userA, CancellationToken.None));

        var count = await lockoutStore.IncrementAccessFailedCountAsync(
            userA, CancellationToken.None);

        Assert.Equal(1, count);

        count = await lockoutStore.IncrementAccessFailedCountAsync(
            userA, CancellationToken.None);

        Assert.Equal(2, count);

        await lockoutStore.ResetAccessFailedCountAsync(
            userA, CancellationToken.None);

        Assert.Equal(
            0,
            await lockoutStore.GetAccessFailedCountAsync(
                userA, CancellationToken.None));

        var lockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);

        await lockoutStore.SetLockoutEndDateAsync(
            userA,
            lockoutEnd,
            CancellationToken.None);

        Assert.Equal(
            lockoutEnd,
            await lockoutStore.GetLockoutEndDateAsync(
                userA, CancellationToken.None));

        await lockoutStore.SetLockoutEnabledAsync(
            userA, false, CancellationToken.None);

        Assert.False(
            await lockoutStore.GetLockoutEnabledAsync(
                userA, CancellationToken.None));

        await lockoutStore.SetLockoutEnabledAsync(
            userA, true, CancellationToken.None);

        // -----------------------------------------
        // 5. Allow Store A to manage its own stamp.
        // -----------------------------------------

        var originalStoreAStamp =
            await securityStampStore.GetSecurityStampAsync(
                userA, CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(originalStoreAStamp));

        const string updatedStamp = "store-a-updated-security-stamp";

        await securityStampStore.SetSecurityStampAsync(
            userA,
            updatedStamp,
            CancellationToken.None);

        Assert.Equal(
            updatedStamp,
            await securityStampStore.GetSecurityStampAsync(
                userA, CancellationToken.None));

        // Persist Store A changes.
        await db.SaveChangesAsync();

        // -----------------------------------------
        // 6. Verify persisted data for both stores.
        // -----------------------------------------

        db.ChangeTracker.Clear();

        var persistedUserA = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userA.Id);

        var persistedUserB = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userB.Id);

        Assert.Equal(0, persistedUserA.AccessFailedCount);
        Assert.True(persistedUserA.LockoutEnabled);
        Assert.Equal(lockoutEnd, persistedUserA.LockoutEnd);
        Assert.Equal(updatedStamp, persistedUserA.SecurityStamp);

        Assert.Equal(0, persistedUserB.AccessFailedCount);
        Assert.True(persistedUserB.LockoutEnabled);
        Assert.Null(persistedUserB.LockoutEnd);
        Assert.Equal(originalStoreBStamp, persistedUserB.SecurityStamp);

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