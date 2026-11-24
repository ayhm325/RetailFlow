
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

public sealed class UserManagerStoreScopingTests
{
    private const string Password = "Valid-Pass123!";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    [Fact]
    public async Task CreateAsync_AllowsSameEmailAcrossStores_AndRejectsDuplicateWithinStore()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using var transaction =
            await connection.BeginTransactionAsync();

        var dbOptions = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(connection)
            .Options;

        var storeA = CreateStore("user-manager-store-a");
        var storeB = CreateStore("user-manager-store-b");

        await using (var setupDb = new IdentitySpikeDbContext(dbOptions))
        {
            await setupDb.Database.UseTransactionAsync(transaction);

            setupDb.Stores.AddRange(storeA, storeB);

            await setupDb.SaveChangesAsync();
        }

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddDbContext<IdentitySpikeDbContext>(options =>
            options.UseSqlServer(connection));

        services.AddScoped<
            IIdentityLookupScopeAccessor,
            IdentityLookupScopeAccessor>();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
            })
            .AddUserStore<ScopedUserStore>();

        await using var serviceProvider =
            services.BuildServiceProvider(validateScopes: true);

        var email = $"{Guid.NewGuid():N}@spike.invalid";

        // Store A: create the first customer.
        await using (var storeAScope = serviceProvider.CreateAsyncScope())
        {
            var accessor = storeAScope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(storeA.Id));

            var db = storeAScope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = storeAScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var result = await userManager.CreateAsync(
                CreateCustomer(storeA.Id, email, "store-a"),
                Password);

            Assert.True(
                result.Succeeded,
                FormatErrors(result));
        }

        // Store B: the same email must be accepted.
        await using (var storeBScope = serviceProvider.CreateAsyncScope())
        {
            var accessor = storeBScope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(storeB.Id));

            var db = storeBScope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = storeBScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var result = await userManager.CreateAsync(
                CreateCustomer(storeB.Id, email, "store-b"),
                Password);

            Assert.True(
                result.Succeeded,
                FormatErrors(result));
        }

        // Store A: the same email must be rejected by SQL Server.
        await using (var storeAScope = serviceProvider.CreateAsyncScope())
        {
            var accessor = storeAScope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(storeA.Id));

            var db = storeAScope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            await db.Database.UseTransactionAsync(transaction);

            var userManager = storeAScope.ServiceProvider
                .GetRequiredService<UserManager<ApplicationUser>>();

            var exception = await Assert.ThrowsAsync<DbUpdateException>(
                () => userManager.CreateAsync(
                    CreateCustomer(storeA.Id, email, "store-a-duplicate"),
                    Password));

            var sqlException = Assert.IsType<SqlException>(
                exception.GetBaseException());

            Assert.True(
                sqlException.Number is 2601 or 2627,
                $"Expected SQL unique constraint error 2601 or 2627, " +
                $"but received {sqlException.Number}.");
        }

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
        string email,
        string prefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        return new ApplicationUser
        {
            Id = uniqueId,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId,
            UserName = $"{prefix}-{uniqueId}",
            Email = email,
            EmailConfirmed = true,
            LockoutEnabled = true
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
