using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class ScopedUserStoreUserPropertiesTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task UserPropertyOperations_RejectForeignAndForgedUsers_AndAllowOwnUser()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using var db = new IdentitySpikeDbContext(options);
        await using var transaction =
            await db.Database.BeginTransactionAsync();

        var storeA = CreateStore("properties-store-a");
        var storeB = CreateStore("properties-store-b");

        var userA = CreateCustomer(storeA.Id, "customer-a");
        var userB = CreateCustomer(storeB.Id, "customer-b");

        db.Stores.AddRange(storeA, storeB);
        db.Users.AddRange(userA, userB);
        await db.SaveChangesAsync();

        var accessor = new IdentityLookupScopeAccessor();
        accessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(storeA.Id));

        using var userStore = new ScopedUserStore(db, accessor);

        IUserStore<ApplicationUser> basicStore = userStore;
        IUserEmailStore<ApplicationUser> emailStore = userStore;
        IUserPasswordStore<ApplicationUser> passwordStore = userStore;

        var ct = CancellationToken.None;

        // -----------------------------------------------------------------
        // 1. Reject Store B through basic IUserStore property operations.
        // -----------------------------------------------------------------

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => basicStore.GetUserIdAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => basicStore.GetUserNameAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => basicStore.GetNormalizedUserNameAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => basicStore.SetUserNameAsync(userB, "hacked", ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => basicStore.SetNormalizedUserNameAsync(userB, "HACKED", ct));

        // -----------------------------------------------------------------
        // 2. Reject Store B through IUserEmailStore.
        // -----------------------------------------------------------------

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.GetEmailAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.SetEmailAsync(userB, "hacked@invalid.test", ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.GetEmailConfirmedAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.SetEmailConfirmedAsync(userB, false, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.GetNormalizedEmailAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.SetNormalizedEmailAsync(
                userB,
                "HACKED@INVALID.TEST",
                ct));

        // -----------------------------------------------------------------
        // 3. Reject Store B through IUserPasswordStore.
        // -----------------------------------------------------------------

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => passwordStore.GetPasswordHashAsync(userB, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => passwordStore.SetPasswordHashAsync(
                userB,
                "unauthorized-hash",
                ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => passwordStore.HasPasswordAsync(userB, ct));

        // -----------------------------------------------------------------
        // 4. Reject a forged object whose in-memory scope claims Store A,
        //    but whose persisted user belongs to Store B.
        // -----------------------------------------------------------------

        var forgedUser = new ApplicationUser
        {
            Id = userB.Id,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeA.Id
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => passwordStore.GetPasswordHashAsync(forgedUser, ct));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => emailStore.SetEmailAsync(
                forgedUser,
                "forged@invalid.test",
                ct));

        // -----------------------------------------------------------------
        // 5. Store A can still read and update its own user.
        // -----------------------------------------------------------------

        Assert.Equal(
            userA.Id,
            await basicStore.GetUserIdAsync(userA, ct));

        Assert.Equal(
            userA.UserName,
            await basicStore.GetUserNameAsync(userA, ct));

        Assert.Equal(
            userA.NormalizedUserName,
            await basicStore.GetNormalizedUserNameAsync(userA, ct));

        Assert.Equal(
            userA.Email,
            await emailStore.GetEmailAsync(userA, ct));

        Assert.True(
            await emailStore.GetEmailConfirmedAsync(userA, ct));

        Assert.Equal(
            userA.NormalizedEmail,
            await emailStore.GetNormalizedEmailAsync(userA, ct));

        Assert.Equal(
            userA.PasswordHash,
            await passwordStore.GetPasswordHashAsync(userA, ct));

        Assert.True(await passwordStore.HasPasswordAsync(userA, ct));

        // Update Store A's values through the same store interfaces.
        await basicStore.SetUserNameAsync(userA, "customer-a-renamed", ct);
        await basicStore.SetNormalizedUserNameAsync(
            userA,
            "CUSTOMER-A-RENAMED",
            ct);

        await emailStore.SetEmailAsync(
            userA,
            "customer-a-updated@spike.invalid",
            ct);

        await emailStore.SetNormalizedEmailAsync(
            userA,
            "CUSTOMER-A-UPDATED@SPIKE.INVALID",
            ct);

        await emailStore.SetEmailConfirmedAsync(userA, false, ct);

        await passwordStore.SetPasswordHashAsync(
            userA,
            "customer-a-updated-hash",
            ct);

        var updateResult = await userStore.UpdateAsync(userA, ct);
        Assert.True(updateResult.Succeeded);

        // -----------------------------------------------------------------
        // 6. Verify persisted state: A changed, B remained untouched.
        // -----------------------------------------------------------------

        db.ChangeTracker.Clear();

        var persistedA = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userA.Id);

        var persistedB = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userB.Id);

        Assert.Equal("customer-a-renamed", persistedA.UserName);
        Assert.Equal("CUSTOMER-A-RENAMED", persistedA.NormalizedUserName);
        Assert.Equal("customer-a-updated@spike.invalid", persistedA.Email);
        Assert.Equal("CUSTOMER-A-UPDATED@SPIKE.INVALID", persistedA.NormalizedEmail);
        Assert.False(persistedA.EmailConfirmed);
        Assert.Equal("customer-a-updated-hash", persistedA.PasswordHash);

        Assert.Equal(userB.UserName, persistedB.UserName);
        Assert.Equal(userB.Email, persistedB.Email);
        Assert.Equal(userB.NormalizedEmail, persistedB.NormalizedEmail);
        Assert.Equal(userB.PasswordHash, persistedB.PasswordHash);
        Assert.True(persistedB.EmailConfirmed);

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
        var id = Guid.NewGuid().ToString("N");
        var email = $"{id}@spike.invalid";
        var userName = $"{prefix}-{id}";

        return new ApplicationUser
        {
            Id = id,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            PasswordHash = $"hash-{prefix}",
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true
        };
    }
}
