using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentityScopeUnitTests.Infrastructure.Identity;

public sealed class UserStoreCapabilitiesTests
{
    [Fact]
    public void UserManager_AdvertisesOnlyMvpStoreCapabilities()
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
            ?? "Server=.;Database=RetailFlowIdentitySpike;" +
               "Trusted_Connection=True;Encrypt=True;" +
               "TrustServerCertificate=True;MultipleActiveResultSets=True";

        var services = new ServiceCollection();

        services.AddLogging();

        services.AddDbContext<IdentitySpikeDbContext>(
            options => options.UseSqlServer(connectionString));

        services.AddScoped<
            IIdentityLookupScopeAccessor,
            IdentityLookupScopeAccessor>();

        services.AddDataProtection();

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = false;
            })
            .AddUserStore<ScopedUserStoreFacade>()
            .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
                TokenOptions.DefaultProvider);

        services.AddScoped<ScopedUserStore>();

        using var serviceProvider =
            services.BuildServiceProvider(validateScopes: true);

        using var scope = serviceProvider.CreateScope();

        var userManager = scope.ServiceProvider
            .GetRequiredService<UserManager<ApplicationUser>>();

        var capabilities = new[]
        {
            // Required by RetailFlow MVP
            ("Password", userManager.SupportsUserPassword, true),
            ("Email", userManager.SupportsUserEmail, true),
            ("SecurityStamp", userManager.SupportsUserSecurityStamp, true),
            ("Lockout", userManager.SupportsUserLockout, true),
            ("Claims", userManager.SupportsUserClaim, true),
            ("QueryableUsers", userManager.SupportsQueryableUsers, true),

            // Outside current MVP scope
            ("AuthenticationTokens", userManager.SupportsUserAuthenticationTokens, false),
            ("ExternalLogins", userManager.SupportsUserLogin, false),
            ("PhoneNumber", userManager.SupportsUserPhoneNumber, false),
            ("TwoFactor", userManager.SupportsUserTwoFactor, false),
            ("AuthenticatorKey", userManager.SupportsUserAuthenticatorKey, false),
            ("RecoveryCodes", userManager.SupportsUserTwoFactorRecoveryCodes, false),
            ("Passkeys", userManager.SupportsUserPasskey, false),
            ("Roles", userManager.SupportsUserRole, false)
        };

        var mismatches = capabilities
            .Where(x => x.Item2 != x.Item3)
            .Select(x =>
                $"{x.Item1}: Expected {x.Item3}, Actual {x.Item2}")
            .ToArray();

        Assert.True(
            mismatches.Length == 0,
            "Unexpected Identity Store capabilities:" +
            Environment.NewLine +
            string.Join(Environment.NewLine, mismatches));
    }
}
