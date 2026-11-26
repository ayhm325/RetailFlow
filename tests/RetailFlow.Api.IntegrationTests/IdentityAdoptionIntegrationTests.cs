using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.Application.Identity;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class IdentityAdoptionIntegrationTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public IdentityAdoptionIntegrationTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task MultiTenant_SameEmailAcrossStores_IsolatedRegistrationLoginAndFailClosedUserStore()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var storeA = Store.Create(Guid.NewGuid(), "Store Alpha", $"alpha-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var storeB = Store.Create(Guid.NewGuid(), "Store Beta", $"beta-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var settingsA = StoreSettings.Create(Guid.NewGuid(), storeA, Jod, "Asia/Amman", true, false);
        var settingsB = StoreSettings.Create(Guid.NewGuid(), storeB, Jod, "Asia/Amman", true, false);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.AddRange(storeA, storeB);
            db.StoreSettings.AddRange(settingsA, settingsB);
            await db.SaveChangesAsync();
        }

        try
        {
            var sharedEmail = $"shared-{suffix}@example.com";

            using var clientA = _fixture.CreateHttpsClient();
            clientA.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var clientB = _fixture.CreateHttpsClient();
            clientB.DefaultRequestHeaders.Add("X-CSRF", "1");

            // 1. Register same email independently in Store A and Store B
            using var regA = await clientA.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/register",
                new RegisterCustomerCommand(sharedEmail, "User Alpha", "Str0ngPass!A"));
            Assert.Equal(HttpStatusCode.Created, regA.StatusCode);

            using var regB = await clientB.PostAsJsonAsync(
                $"/api/stores/{storeB.Slug}/auth/register",
                new RegisterCustomerCommand(sharedEmail, "User Beta", "Str0ngPass!B"));
            Assert.Equal(HttpStatusCode.Created, regB.StatusCode);

            var dtoA = await regA.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            var dtoB = await regB.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            Assert.NotNull(dtoA);
            Assert.NotNull(dtoB);
            Assert.NotEqual(dtoA!.UserId, dtoB!.UserId);

            // 2. Store A password cannot log into Store B
            using var anonClient = _fixture.CreateHttpsClient();
            using var crossLogin = await anonClient.PostAsJsonAsync(
                $"/api/stores/{storeB.Slug}/auth/login",
                new LoginCommand(sharedEmail, "Str0ngPass!A"));
            Assert.Equal(HttpStatusCode.Unauthorized, crossLogin.StatusCode);

            // 3. Store A cookie is rejected on Store B /auth/me with 403 Forbidden (preserving Store A session)
            using var crossMe = await clientA.GetAsync($"/api/stores/{storeB.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Forbidden, crossMe.StatusCode);

            // 4. ScopedUserStore fails closed without IdentityLookupScope and blocks cross-store lookup/role escalation
            await using var scope = _fixture.CreateAsyncScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
            var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => userManager.FindByEmailAsync(sharedEmail));

            scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(storeA.Id));
            var foundInA = await userManager.FindByEmailAsync(sharedEmail);
            Assert.NotNull(foundInA);
            Assert.Equal(dtoA.UserId, foundInA!.Id);

            // Cannot find Store B user by ID from Store A scope
            Assert.Null(await userManager.FindByIdAsync(dtoB.UserId.ToString()));

            // Cannot assign PlatformAdmin role in Store scope
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => userManager.AddToRoleAsync(foundInA, IdentityRoleNames.PlatformAdmin));
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task Identity_Lockout_PasswordReset_SecurityStampRevocation_And_TwoFactor_AllWork()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "Sec Shop", $"sec-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, false);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        try
        {
            var email = $"sec-{suffix}@example.com";
            using var client = _fixture.CreateHttpsClient();
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var reg = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/register",
                new RegisterCustomerCommand(email, "Sec User", "Str0ngPass!1"));
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
            var userDto = await reg.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            Assert.NotNull(userDto);

            // 1. Forgot-password returns generic 202 for both existing and unknown emails
            using var forgotKnown = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/forgot-password",
                new ForgotPasswordCommand(email));
            Assert.Equal(HttpStatusCode.Accepted, forgotKnown.StatusCode);

            using var forgotUnknown = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/forgot-password",
                new ForgotPasswordCommand($"unknown-{suffix}@example.com"));
            Assert.Equal(HttpStatusCode.Accepted, forgotUnknown.StatusCode);

            // 2. Password reset updates SecurityStamp, deletes SessionTickets, and invalidates existing cookie
            string resetToken;
            await using (var scope = _fixture.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(store.Id));
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
                var user = await userManager.FindByIdAsync(userDto!.UserId.ToString());
                Assert.NotNull(user);
                resetToken = await userManager.GeneratePasswordResetTokenAsync(user!);
            }

            using var resetResp = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/reset-password",
                new ResetPasswordCommand(email, resetToken, "NewStr0ngPass!2"));
            Assert.Equal(HttpStatusCode.NoContent, resetResp.StatusCode);

            using var staleMe = await client.GetAsync($"/api/stores/{store.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, staleMe.StatusCode);

            // 3. Account Lockout after 5 failed attempts (returns 423 Locked even with valid password)
            using var lockoutClient = _fixture.CreateHttpsClient();
            lockoutClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            for (var i = 0; i < 4; i++)
            {
                using var bad = await lockoutClient.PostAsJsonAsync(
                    $"/api/stores/{store.Slug}/auth/login",
                    new LoginCommand(email, "WrongPass!99"));
                Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
            }

            using var fifthFail = await lockoutClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "WrongPass!99"));
            Assert.Equal(HttpStatusCode.Locked, fifthFail.StatusCode);

            using var validDuringLockout = await lockoutClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "NewStr0ngPass!2"));
            Assert.Equal(HttpStatusCode.Locked, validDuringLockout.StatusCode);

            // Clear lockout and test 2FA setup + recovery code sign-in
            await using (var scope = _fixture.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(store.Id));
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
                var user = await userManager.FindByIdAsync(userDto!.UserId.ToString());
                await userManager.SetLockoutEndDateAsync(user!, null);
                await userManager.ResetAccessFailedCountAsync(user!);
            }

            using var loginOk = await lockoutClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "NewStr0ngPass!2"));
            Assert.Equal(HttpStatusCode.OK, loginOk.StatusCode);

            using var setup2fa = await lockoutClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/setup",
                new BeginTwoFactorSetupCommand("NewStr0ngPass!2"));
            Assert.Equal(HttpStatusCode.OK, setup2fa.StatusCode);
            var setupDto = await setup2fa.Content.ReadFromJsonAsync<TwoFactorSetupDto>();
            Assert.NotNull(setupDto);
            Assert.StartsWith("otpauth://totp/", setupDto!.AuthenticatorUri, StringComparison.Ordinal);

            // Enable 2FA and generate recovery codes via UserManager (simulating verified authenticator app)
            IReadOnlyList<string> recoveryCodes;
            await using (var scope = _fixture.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(store.Id));
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
                var user = await userManager.FindByIdAsync(userDto!.UserId.ToString());
                await userManager.SetTwoFactorEnabledAsync(user!, true);
                var codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(user!, 5);
                recoveryCodes = codes!.ToList();
            }

            using var freshClient = _fixture.CreateHttpsClient();
            using var missing2fa = await freshClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "NewStr0ngPass!2"));
            Assert.Equal(HttpStatusCode.Unauthorized, missing2fa.StatusCode);

            using var recoveryLogin = await freshClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "NewStr0ngPass!2", RecoveryCode: recoveryCodes[0]));
            Assert.Equal(HttpStatusCode.OK, recoveryLogin.StatusCode);

            using var meAfter2fa = await freshClient.GetAsync($"/api/stores/{store.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.OK, meAfter2fa.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }
}
