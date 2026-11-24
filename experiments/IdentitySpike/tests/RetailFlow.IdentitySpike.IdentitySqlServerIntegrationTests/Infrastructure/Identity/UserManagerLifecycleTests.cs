using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class UserManagerLifecycleTests
{
    private const string Password = "Valid-Pass123!";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task ConfirmEmailAsync_ConfirmsOnlyWithValidToken_AndPersistsToSqlServer()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        var dbOptions =
            new DbContextOptionsBuilder<IdentitySpikeDbContext>()
                .UseSqlServer(connection)
                .Options;

        var store = CreateStore();

        await using (var setupDb = new IdentitySpikeDbContext(dbOptions))
        {
            await setupDb.Database.UseTransactionAsync(transaction);

            setupDb.Stores.Add(store);

            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();

        services.AddDbContext<IdentitySpikeDbContext>(
            options => options.UseSqlServer(connection));

        services.AddScoped<
            IIdentityLookupScopeAccessor,
            IdentityLookupScopeAccessor>();

        services.AddScoped<ScopedUserStore>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
            })
            .AddUserStore<ScopedUserStoreFacade>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
                TokenOptions.DefaultProvider);

        await using var serviceProvider =
            services.BuildServiceProvider(validateScopes: true);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var accessor = scope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(store.Id));

            var db = scope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var email = $"{Guid.NewGuid():N}@spike.invalid";

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountKind = AccountKind.StoreCustomer,
                StoreId = store.Id,
                UserName = $"customer-{Guid.NewGuid():N}",
                Email = email,
                EmailConfirmed = false,
                LockoutEnabled = true
            };

            // Arrange: create an unconfirmed user.
            var createResult = await userManager.CreateAsync(
                user,
                Password);

            Assert.True(
                createResult.Succeeded,
                FormatErrors(createResult));

            Assert.False(
                await userManager.IsEmailConfirmedAsync(user));

            // Act: generate the email confirmation token.
            var token = await userManager.GenerateEmailConfirmationTokenAsync(
                user);

            Assert.False(string.IsNullOrWhiteSpace(token));

            // Assert: an invalid token must not confirm the email.
            var invalidResult = await userManager.ConfirmEmailAsync(
                user,
                token + "invalid");

            Assert.False(invalidResult.Succeeded);

            Assert.False(
                await userManager.IsEmailConfirmedAsync(user));

            // Act: confirm with the valid token.
            var confirmationResult = await userManager.ConfirmEmailAsync(
                user,
                token);

            Assert.True(
                confirmationResult.Succeeded,
                FormatErrors(confirmationResult));

            Assert.True(
                await userManager.IsEmailConfirmedAsync(user));

            // Assert persistence using a separate DbContext.
            await using var verificationDb =
                new IdentitySpikeDbContext(dbOptions);

            await verificationDb.Database.UseTransactionAsync(transaction);

            var persistedUser = await verificationDb
                .Set<ApplicationUser>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);

            Assert.True(persistedUser.EmailConfirmed);
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task ResetPasswordAsync_ChangesPassword_AndInvalidatesPreviousToken()
    {
        const string newPassword = "New-Valid-Pass456!";
        const string anotherPassword = "Another-Valid789!";

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        var dbOptions =
            new DbContextOptionsBuilder<IdentitySpikeDbContext>()
                .UseSqlServer(connection)
                .Options;

        var store = CreateStore();

        await using (var setupDb = new IdentitySpikeDbContext(dbOptions))
        {
            await setupDb.Database.UseTransactionAsync(transaction);

            setupDb.Stores.Add(store);
            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();

        services.AddDbContext<IdentitySpikeDbContext>(
            options => options.UseSqlServer(connection));

        services.AddScoped<
            IIdentityLookupScopeAccessor,
            IdentityLookupScopeAccessor>();

        services.AddScoped<ScopedUserStore>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
            })
            .AddUserStore<ScopedUserStoreFacade>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
                TokenOptions.DefaultProvider);

        await using var serviceProvider =
            services.BuildServiceProvider(validateScopes: true);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var accessor = scope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(store.Id));

            var db = scope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountKind = AccountKind.StoreCustomer,
                StoreId = store.Id,
                UserName = $"customer-{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@spike.invalid",
                EmailConfirmed = true,
                LockoutEnabled = true
            };

            // Arrange: create user with the original password.
            var createResult = await userManager.CreateAsync(
                user,
                Password);

            Assert.True(
                createResult.Succeeded,
                FormatErrors(createResult));

            Assert.True(
                await userManager.CheckPasswordAsync(user, Password));

            var originalSecurityStamp =
                await userManager.GetSecurityStampAsync(user);

            // Generate password reset token.
            var resetToken =
                await userManager.GeneratePasswordResetTokenAsync(user);

            Assert.False(string.IsNullOrWhiteSpace(resetToken));

            // An invalid token must not change the password.
            var invalidResult = await userManager.ResetPasswordAsync(
                user,
                resetToken + "invalid",
                newPassword);

            Assert.False(invalidResult.Succeeded);

            Assert.True(
                await userManager.CheckPasswordAsync(user, Password));

            // Reset with the valid token.
            var resetResult = await userManager.ResetPasswordAsync(
                user,
                resetToken,
                newPassword);

            Assert.True(
                resetResult.Succeeded,
                FormatErrors(resetResult));

            var updatedSecurityStamp =
                await userManager.GetSecurityStampAsync(user);

            Assert.NotEqual(
                originalSecurityStamp,
                updatedSecurityStamp);

            // The original password must no longer work.
            Assert.False(
                await userManager.CheckPasswordAsync(user, Password));

            // The new password must work.
            Assert.True(
                await userManager.CheckPasswordAsync(user, newPassword));

            // Reusing the old token must fail after the security stamp changes.
            var reusedTokenResult = await userManager.ResetPasswordAsync(
                user,
                resetToken,
                anotherPassword);

            Assert.False(reusedTokenResult.Succeeded);

            // Verify that the password hash was persisted to SQL Server.
            await using var verificationDb =
                new IdentitySpikeDbContext(dbOptions);

            await verificationDb.Database.UseTransactionAsync(transaction);

            var persistedUser = await verificationDb
                .Set<ApplicationUser>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);

            Assert.True(
                await userManager.CheckPasswordAsync(
                    persistedUser,
                    newPassword));

            Assert.False(
                await userManager.CheckPasswordAsync(
                    persistedUser,
                    Password));

            Assert.Equal(
                updatedSecurityStamp,
                persistedUser.SecurityStamp);
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task AccessFailedAsync_LocksUserAtConfiguredThreshold_AndPersistsLockout()
    {
        const int maxFailedAttempts = 3;
        var lockoutDuration = TimeSpan.FromMinutes(5);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        var dbOptions =
            new DbContextOptionsBuilder<IdentitySpikeDbContext>()
                .UseSqlServer(connection)
                .Options;

        var store = CreateStore();

        await using (var setupDb = new IdentitySpikeDbContext(dbOptions))
        {
            await setupDb.Database.UseTransactionAsync(transaction);

            setupDb.Stores.Add(store);
            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();

        services.AddDbContext<IdentitySpikeDbContext>(
            options => options.UseSqlServer(connection));

        services.AddScoped<
            IIdentityLookupScopeAccessor,
            IdentityLookupScopeAccessor>();

        services.AddScoped<ScopedUserStore>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = maxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan = lockoutDuration;
            })
            .AddUserStore<ScopedUserStoreFacade>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
                TokenOptions.DefaultProvider);

        await using var serviceProvider =
            services.BuildServiceProvider(validateScopes: true);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var accessor = scope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(store.Id));

            var db = scope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountKind = AccountKind.StoreCustomer,
                StoreId = store.Id,
                UserName = $"customer-{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@spike.invalid",
                EmailConfirmed = true,
                LockoutEnabled = true
            };

            // Arrange: create a lockout-enabled user.
            var createResult = await userManager.CreateAsync(
                user,
                Password);

            Assert.True(
                createResult.Succeeded,
                FormatErrors(createResult));

            Assert.False(await userManager.IsLockedOutAsync(user));
            Assert.Equal(0, await userManager.GetAccessFailedCountAsync(user));

            // First failed attempt.
            var firstResult = await userManager.AccessFailedAsync(user);

            Assert.True(firstResult.Succeeded);
            Assert.Equal(1, await userManager.GetAccessFailedCountAsync(user));
            Assert.False(await userManager.IsLockedOutAsync(user));

            // Second failed attempt.
            var secondResult = await userManager.AccessFailedAsync(user);

            Assert.True(secondResult.Succeeded);
            Assert.Equal(2, await userManager.GetAccessFailedCountAsync(user));
            Assert.False(await userManager.IsLockedOutAsync(user));

            // Third failed attempt reaches the configured threshold.
            var beforeLockout = DateTimeOffset.UtcNow;

            var thirdResult = await userManager.AccessFailedAsync(user);

            Assert.True(
                thirdResult.Succeeded,
                FormatErrors(thirdResult));

            // The account must now be locked.
            Assert.True(await userManager.IsLockedOutAsync(user));

            // Identity resets the failed count after lockout.
            Assert.Equal(0, await userManager.GetAccessFailedCountAsync(user));

            var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);

            Assert.NotNull(lockoutEnd);

            Assert.True(
                lockoutEnd >= beforeLockout.Add(lockoutDuration),
                $"Expected lockout end to be at least " +
                $"{beforeLockout.Add(lockoutDuration):O}, " +
                $"but received {lockoutEnd:O}.");

            // Verify persisted state using a separate DbContext.
            await using var verificationDb =
                new IdentitySpikeDbContext(dbOptions);

            await verificationDb.Database.UseTransactionAsync(transaction);

            var persistedUser = await verificationDb
                .Set<ApplicationUser>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);

            Assert.True(persistedUser.LockoutEnabled);
            Assert.Equal(0, persistedUser.AccessFailedCount);
            Assert.Equal(lockoutEnd, persistedUser.LockoutEnd);

            // Simulate an expired lockout without waiting five minutes.
            var expiredLockoutEnd = DateTimeOffset.UtcNow.AddSeconds(-1);

            var unlockResult = await userManager.SetLockoutEndDateAsync(
                user,
                expiredLockoutEnd);

            Assert.True(
                unlockResult.Succeeded,
                FormatErrors(unlockResult));

            // The account must no longer be locked.
            Assert.False(await userManager.IsLockedOutAsync(user));

            // Verify the expired lockout date was persisted.
            await using var expiredVerificationDb =
                new IdentitySpikeDbContext(dbOptions);

            await expiredVerificationDb.Database.UseTransactionAsync(transaction);

            var userAfterExpiry = await expiredVerificationDb
                .Set<ApplicationUser>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);

            Assert.NotNull(userAfterExpiry.LockoutEnd);
            Assert.True(userAfterExpiry.LockoutEnd < DateTimeOffset.UtcNow);
        }

        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task CheckPasswordSignInAsync_RespectsLockoutOnFailure()
    {
        const int maxFailedAttempts = 3;
        const string wrongPassword = "Wrong-Password123!";

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        var dbOptions =
            new DbContextOptionsBuilder<IdentitySpikeDbContext>()
                .UseSqlServer(connection)
                .Options;

        var store = CreateStore();

        await using (var setupDb = new IdentitySpikeDbContext(dbOptions))
        {
            await setupDb.Database.UseTransactionAsync(transaction);

            setupDb.Stores.Add(store);
            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDataProtection();
        services.AddHttpContextAccessor();
        services.AddAuthentication();

        services.AddDbContext<IdentitySpikeDbContext>(
            options => options.UseSqlServer(connection));

        services.AddScoped<
            IIdentityLookupScopeAccessor,
            IdentityLookupScopeAccessor>();

        services.AddScoped<ScopedUserStore>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;

                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = maxFailedAttempts;
                options.Lockout.DefaultLockoutTimeSpan =
                    TimeSpan.FromMinutes(5);
            })
            .AddUserStore<ScopedUserStoreFacade>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
                TokenOptions.DefaultProvider)
            .AddSignInManager();

        await using var serviceProvider =
            services.BuildServiceProvider(validateScopes: true);

        await using (var scope = serviceProvider.CreateAsyncScope())
        {
            var accessor = scope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(store.Id));

            var db = scope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = scope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var signInManager = scope.ServiceProvider
                .GetRequiredService<SignInManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString("N"),
                AccountKind = AccountKind.StoreCustomer,
                StoreId = store.Id,
                UserName = $"customer-{Guid.NewGuid():N}",
                Email = $"{Guid.NewGuid():N}@spike.invalid",
                EmailConfirmed = true,
                LockoutEnabled = true
            };

            var createResult = await userManager.CreateAsync(
                user,
                Password);

            Assert.True(
                createResult.Succeeded,
                FormatErrors(createResult));

            // 1. Wrong password with lockoutOnFailure = false.
            var noLockoutResult =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    wrongPassword,
                    lockoutOnFailure: false);

            Assert.False(noLockoutResult.Succeeded);
            Assert.False(noLockoutResult.IsLockedOut);

            Assert.Equal(
                0,
                await userManager.GetAccessFailedCountAsync(user));

            // 2. Wrong password with lockoutOnFailure = true.
            var firstFailure =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    wrongPassword,
                    lockoutOnFailure: true);

            Assert.False(firstFailure.Succeeded);
            Assert.False(firstFailure.IsLockedOut);

            Assert.Equal(
                1,
                await userManager.GetAccessFailedCountAsync(user));

            // 3. A successful password check resets the failed count.
            var successfulResult =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    Password,
                    lockoutOnFailure: true);

            Assert.True(successfulResult.Succeeded);

            Assert.Equal(
                0,
                await userManager.GetAccessFailedCountAsync(user));

            // 4. Three consecutive failures must lock the account.
            var secondFailure =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    wrongPassword,
                    lockoutOnFailure: true);

            Assert.False(secondFailure.IsLockedOut);

            var thirdFailure =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    wrongPassword,
                    lockoutOnFailure: true);

            Assert.False(thirdFailure.IsLockedOut);

            var fourthFailure =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    wrongPassword,
                    lockoutOnFailure: true);

            Assert.True(fourthFailure.IsLockedOut);
            Assert.False(fourthFailure.Succeeded);

            Assert.True(
                await userManager.IsLockedOutAsync(user));

            // 5. Even the correct password must be rejected while locked.
            var correctPasswordWhileLocked =
                await signInManager.CheckPasswordSignInAsync(
                    user,
                    Password,
                    lockoutOnFailure: true);

            Assert.True(correctPasswordWhileLocked.IsLockedOut);
            Assert.False(correctPasswordWhileLocked.Succeeded);

            // 6. Verify persisted lockout state.
            await using var verificationDb =
                new IdentitySpikeDbContext(dbOptions);

            await verificationDb.Database.UseTransactionAsync(transaction);

            var persistedUser = await verificationDb
                .Set<ApplicationUser>()
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == user.Id);

            Assert.NotNull(persistedUser.LockoutEnd);
            Assert.True(persistedUser.LockoutEnd > DateTimeOffset.UtcNow);
            Assert.Equal(0, persistedUser.AccessFailedCount);
        }

        await transaction.RollbackAsync();
    }

    private static SpikeStore CreateStore()
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        return new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = $"lifecycle-store-{uniqueId}",
            Slug = $"lifecycle-store-{uniqueId}",
            IsActive = true
        };
    }

    private static string FormatErrors(IdentityResult result)
    {
        return string.Join(
            "; ",
            result.Errors.Select(error =>
                $"{error.Code}: {error.Description}"));
    }
}
