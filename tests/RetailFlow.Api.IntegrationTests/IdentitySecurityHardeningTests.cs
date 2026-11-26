using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Web;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Currencies;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

public sealed class IdentitySecurityHardeningTests : IClassFixture<CatalogSqlTestFixture>
{
    private static readonly Currency Jod = Currency.Create("JOD", "400", 3);
    private readonly CatalogSqlTestFixture _fixture;

    public IdentitySecurityHardeningTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class CapturingEmailSender : IRetailFlowEmailSender
    {
        public ConcurrentQueue<(string To, string Link)> PasswordResets { get; } = new();
        public ConcurrentQueue<(string To, string Link)> EmailConfirmations { get; } = new();
        public ConcurrentQueue<(string To, string Subject)> SecurityNotifications { get; } = new();

        public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken)
        {
            PasswordResets.Enqueue((toEmail, resetLink));
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken)
        {
            EmailConfirmations.Enqueue((toEmail, confirmationLink));
            return Task.CompletedTask;
        }

        public Task SendSecurityNotificationAsync(string toEmail, string subject, string message, CancellationToken cancellationToken)
        {
            SecurityNotifications.Enqueue((toEmail, subject));
            return Task.CompletedTask;
        }
    }

    private WebApplicationFactory<Program> CreateHardenedFactory(
        CapturingEmailSender emailSender,
        bool requireAdminMfa = false)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["Email:PublicBaseUrl"] = "https://shop.retailflow.test",
                        ["PlatformSecurity:RequireAdminMfa"] = requireAdminMfa ? "true" : "false"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IRetailFlowEmailSender>();
                    services.AddSingleton<IRetailFlowEmailSender>(emailSender);
                });
            });
    }

    private static string ExtractSecretFromUri(string authenticatorUri)
    {
        var uri = new Uri(authenticatorUri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query["secret"] ?? throw new InvalidOperationException("Missing secret in authenticator URI.");
    }

    private static string ComputeTotp(string base32Secret, DateTimeOffset utcNow) =>
        TotpCryptoHelper.ComputeTotpForTime(base32Secret, utcNow).ToString("D6");

    [Fact]
    public async Task TotpEnrollment_SecretProtection_StepUp_Replacement_Replay_BruteForce_And_ConcurrentRecovery_AllVerified()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateHardenedFactory(emailSender);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var store = Store.Create(Guid.NewGuid(), "MFA Shop", $"mfa-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var settings = StoreSettings.Create(Guid.NewGuid(), store, Jod, "Asia/Amman", true, false);

        await using (var db = _fixture.CreateDbContext())
        {
            db.Stores.Add(store);
            db.StoreSettings.Add(settings);
            await db.SaveChangesAsync();
        }

        try
        {
            var email = $"mfa-{suffix}@example.com";
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var reg = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/register",
                new RegisterCustomerCommand(email, "MFA User", "Str0ngPassphrase!1"));
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
            var userDto = await reg.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            Assert.NotNull(userDto);

            // 1. Setup with wrong password is rejected
            using var badSetup = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/setup",
                new BeginTwoFactorSetupCommand("WrongPass!1"));
            Assert.Equal(HttpStatusCode.BadRequest, badSetup.StatusCode);

            // 2. First setup with valid password succeeds
            using var setupResp = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/setup",
                new BeginTwoFactorSetupCommand("Str0ngPassphrase!1"));
            Assert.Equal(HttpStatusCode.OK, setupResp.StatusCode);
            var setup = await setupResp.Content.ReadFromJsonAsync<TwoFactorSetupDto>();
            Assert.NotNull(setup);
            var secret1 = ExtractSecretFromUri(setup!.AuthenticatorUri);

            // 3. Invalid TOTP cannot enable MFA
            using var badEnable = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/enable",
                new EnableTwoFactorCommand("000000"));
            Assert.Equal(HttpStatusCode.BadRequest, badEnable.StatusCode);

            // 4. Valid TOTP enables MFA and returns 10 recovery codes
            var code1 = ComputeTotp(secret1, DateTimeOffset.UtcNow);
            using var enableResp = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/enable",
                new EnableTwoFactorCommand(code1));
            Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);
            var recovery1 = await enableResp.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesDto>();
            Assert.NotNull(recovery1);
            Assert.Equal(10, recovery1!.RecoveryCodes.Count);

            // 5. Repeated /2fa/setup on enrolled account is REJECTED (never leaks existing secret)
            using var repeatSetup = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/setup",
                new BeginTwoFactorSetupCommand("Str0ngPassphrase!1"));
            Assert.Equal(HttpStatusCode.BadRequest, repeatSetup.StatusCode);

            // 6. Verify AuthenticatorKey is encrypted at rest in AspNetUserTokens (dp:v1: prefix)
            await using (var db = _fixture.CreateDbContext())
            {
                var tokenRow = await db.UserTokens
                    .AsNoTracking()
                    .SingleAsync(t => t.UserId == userDto!.UserId && t.Name == "AuthenticatorKey");
                Assert.NotNull(tokenRow.Value);
                Assert.StartsWith("dp:v1:", tokenRow.Value, StringComparison.Ordinal);
                Assert.DoesNotContain(secret1, tokenRow.Value, StringComparison.Ordinal);
            }

            // 7. TOTP Replay Prevention: using the same time-step code again is rejected
            using var regenReplay = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/recovery-codes",
                new RegenerateRecoveryCodesCommand(code1));
            Assert.Equal(HttpStatusCode.BadRequest, regenReplay.StatusCode);

            // 8. Concurrent recovery code reuse: only ONE succeeds, the other is rejected
            using var c1 = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            using var c2 = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            var sharedRecoveryCode = recovery1.RecoveryCodes[0];

            var concurrentResults = await Task.WhenAll(
                c1.PostAsJsonAsync($"/api/stores/{store.Slug}/auth/login", new LoginCommand(email, "Str0ngPassphrase!1", RecoveryCode: sharedRecoveryCode)),
                c2.PostAsJsonAsync($"/api/stores/{store.Slug}/auth/login", new LoginCommand(email, "Str0ngPassphrase!1", RecoveryCode: sharedRecoveryCode)));

            Assert.Equal(1, concurrentResults.Count(r => r.StatusCode == HttpStatusCode.OK));
            Assert.Equal(1, concurrentResults.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));

            // Reset MFA counter after concurrent recovery failure before testing the exact 5-attempt TOTP lockout threshold
            await using (var db = _fixture.CreateDbContext())
            {
                await db.Users
                    .Where(u => u.Id == userDto!.UserId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.MfaFailedAttempts, 0)
                        .SetProperty(u => u.MfaLockoutEndUtc, (DateTimeOffset?)null));
            }

            // 9. TOTP Brute-Force Lockout (5 wrong TOTP attempts -> 423 Locked even with valid password & valid code)
            using var bruteClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            for (var i = 0; i < 4; i++)
            {
                using var badTotp = await bruteClient.PostAsJsonAsync(
                    $"/api/stores/{store.Slug}/auth/login",
                    new LoginCommand(email, "Str0ngPassphrase!1", TwoFactorCode: "999999"));
                Assert.Equal(HttpStatusCode.Unauthorized, badTotp.StatusCode);
            }

            using var fifthBadTotp = await bruteClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "Str0ngPassphrase!1", TwoFactorCode: "999999"));
            Assert.Equal(HttpStatusCode.Locked, fifthBadTotp.StatusCode);

            // Clear MFA lockout for replacement & disable verification
            await using (var db = _fixture.CreateDbContext())
            {
                await db.Users
                    .Where(u => u.Id == userDto!.UserId)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.MfaFailedAttempts, 0)
                        .SetProperty(u => u.MfaLockoutEndUtc, (DateTimeOffset?)null));
            }

            // 10. Authenticator Replacement requires Step-up (using recovery code since current step was consumed)
            using var replaceSetupBad = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/replace/setup",
                new BeginTwoFactorReplacementCommand("Str0ngPassphrase!1", CurrentTwoFactorCode: "000000"));
            Assert.Equal(HttpStatusCode.BadRequest, replaceSetupBad.StatusCode);

            using var replaceSetupOk = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/replace/setup",
                new BeginTwoFactorReplacementCommand("Str0ngPassphrase!1", RecoveryCode: recovery1.RecoveryCodes[1]));
            Assert.Equal(HttpStatusCode.OK, replaceSetupOk.StatusCode);
            var replaceDto = await replaceSetupOk.Content.ReadFromJsonAsync<TwoFactorSetupDto>();
            Assert.NotNull(replaceDto);
            var secret2 = ExtractSecretFromUri(replaceDto!.AuthenticatorUri);
            Assert.NotEqual(secret1, secret2);

            var newCode2 = ComputeTotp(secret2, DateTimeOffset.UtcNow);
            using var confirmReplace = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/replace/confirm",
                new ConfirmTwoFactorReplacementCommand(newCode2));
            Assert.Equal(HttpStatusCode.OK, confirmReplace.StatusCode);
            var recovery2 = await confirmReplace.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesDto>();
            Assert.NotNull(recovery2);

            // Old session cookie was revoked on replacement
            using var postReplaceMe = await client.GetAsync($"/api/stores/{store.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, postReplaceMe.StatusCode);

            // Old recovery code from recovery1 no longer works
            using var oldRecoveryLogin = await bruteClient.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "Str0ngPassphrase!1", RecoveryCode: recovery1.RecoveryCodes[2]));
            Assert.Equal(HttpStatusCode.Unauthorized, oldRecoveryLogin.StatusCode);

            // Login with new recovery2 code succeeds, and Step-up Disable MFA revokes session
            using var newLogin = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/login",
                new LoginCommand(email, "Str0ngPassphrase!1", RecoveryCode: recovery2!.RecoveryCodes[0]));
            Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

            using var disableBad = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/disable",
                new DisableTwoFactorCommand("Str0ngPassphrase!1", TwoFactorCode: "000000"));
            Assert.Equal(HttpStatusCode.BadRequest, disableBad.StatusCode);

            using var disableOk = await client.PostAsJsonAsync(
                $"/api/stores/{store.Slug}/auth/2fa/disable",
                new DisableTwoFactorCommand("Str0ngPassphrase!1", RecoveryCode: recovery2.RecoveryCodes[1]));
            Assert.Equal(HttpStatusCode.NoContent, disableOk.StatusCode);

            using var postDisableMe = await client.GetAsync($"/api/stores/{store.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, postDisableMe.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task PasswordReset_EmailDelivery_SingleUse_SessionRevocation_And_CrossTenantIsolation()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateHardenedFactory(emailSender);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var now = DateTimeOffset.UtcNow;
        var storeA = Store.Create(Guid.NewGuid(), "Reset A", $"reseta-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
        var storeB = Store.Create(Guid.NewGuid(), "Reset B", $"resetb-{suffix}", StoreType.OnlineOnly, StoreStatus.Active, now);
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
            var sharedEmail = $"reset-{suffix}@example.com";
            using var clientA = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            clientA.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var clientB = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            clientB.DefaultRequestHeaders.Add("X-CSRF", "1");

            await clientA.PostAsJsonAsync($"/api/stores/{storeA.Slug}/auth/register", new RegisterCustomerCommand(sharedEmail, "User A", "OldPassphraseA!1"));
            await clientB.PostAsJsonAsync($"/api/stores/{storeB.Slug}/auth/register", new RegisterCustomerCommand(sharedEmail, "User B", "OldPassphraseB!1"));

            // Unknown email returns same 202 Accepted without sending email
            using var unknownForgot = await clientA.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/forgot-password",
                new ForgotPasswordCommand($"nobody-{suffix}@example.com"));
            Assert.Equal(HttpStatusCode.Accepted, unknownForgot.StatusCode);
            Assert.Empty(emailSender.PasswordResets);

            // Known email in Store A dispatches real email with protected token link
            using var knownForgot = await clientA.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/forgot-password",
                new ForgotPasswordCommand(sharedEmail));
            Assert.Equal(HttpStatusCode.Accepted, knownForgot.StatusCode);

            Assert.True(emailSender.PasswordResets.TryDequeue(out var dispatched));
            Assert.Equal(sharedEmail, dispatched.To);
            var resetUri = new Uri(dispatched.Link);
            var token = HttpUtility.ParseQueryString(resetUri.Query)["token"];
            Assert.False(string.IsNullOrWhiteSpace(token));

            // Cross-tenant isolation: Store A's reset token CANNOT reset Store B's account
            using var crossReset = await clientB.PostAsJsonAsync(
                $"/api/stores/{storeB.Slug}/auth/reset-password",
                new ResetPasswordCommand(sharedEmail, token!, "CrossPassphrase!9"));
            Assert.Equal(HttpStatusCode.BadRequest, crossReset.StatusCode);

            // Store A reset succeeds, revokes Store A session, and sends security notification
            using var validReset = await clientA.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/reset-password",
                new ResetPasswordCommand(sharedEmail, token!, "NewPassphraseA!2"));
            Assert.Equal(HttpStatusCode.NoContent, validReset.StatusCode);

            using var revokedSession = await clientA.GetAsync($"/api/stores/{storeA.Slug}/auth/me");
            Assert.Equal(HttpStatusCode.Unauthorized, revokedSession.StatusCode);
            Assert.Contains(emailSender.SecurityNotifications, n => n.To == sharedEmail);

            // Token cannot be reused
            using var reuseReset = await clientA.PostAsJsonAsync(
                $"/api/stores/{storeA.Slug}/auth/reset-password",
                new ResetPasswordCommand(sharedEmail, token!, "AnotherPassphrase!3"));
            Assert.Equal(HttpStatusCode.BadRequest, reuseReset.StatusCode);

            // Store B's password remains untouched
            using var loginB = await clientB.PostAsJsonAsync(
                $"/api/stores/{storeB.Slug}/auth/login",
                new LoginCommand(sharedEmail, "OldPassphraseB!1"));
            Assert.Equal(HttpStatusCode.OK, loginB.StatusCode);
        }
        finally
        {
            await _fixture.DeleteStoresAsync(storeA.Id, storeB.Id);
        }
    }

    [Fact]
    public async Task PlatformAdmin_RequireAdminMfa_BlocksPrivilegedEndpointsUntilMfaEnrolledAndVerified()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateHardenedFactory(emailSender, requireAdminMfa: true);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var adminEmail = $"platmfa-{suffix}@retailflow.test";

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
            scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<RetailFlowIdentityRole>>();

            if (!await roleManager.RoleExistsAsync(IdentityRoleNames.PlatformAdmin))
            {
                await roleManager.CreateAsync(new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.PlatformAdminRoleId,
                    Name = IdentityRoleNames.PlatformAdmin,
                    NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                });
            }

            var admin = new RetailFlowIdentityUser
            {
                Id = adminId,
                UserName = adminId.ToString("N"),
                Email = adminEmail,
                DisplayName = "Enforced Admin",
                StoreId = null,
                IsActive = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            Assert.True((await userManager.CreateAsync(admin, "Adm1nPassphrase!99")).Succeeded);
            Assert.True((await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin)).Succeeded);
        }

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            // 1. Login without MFA returns MfaRequiredForPlatformAccess = true
            using var loginResp = await client.PostAsJsonAsync(
                "/api/platform/auth/login",
                new LoginCommand(adminEmail, "Adm1nPassphrase!99"));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var loginDto = await loginResp.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            Assert.NotNull(loginDto);
            Assert.True(loginDto!.MfaRequiredForPlatformAccess);

            // 2. Privileged /api/platform/stores is FORBIDDEN until MFA is enrolled & verified
            using var blockedStoreCreate = await client.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Blocked", $"blocked-{suffix}", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Forbidden, blockedStoreCreate.StatusCode);

            // 3. Restricted enrollment endpoints ARE accessible
            using var setupResp = await client.PostAsJsonAsync(
                "/api/platform/auth/2fa/setup",
                new BeginTwoFactorSetupCommand("Adm1nPassphrase!99"));
            Assert.Equal(HttpStatusCode.OK, setupResp.StatusCode);
            var setup = await setupResp.Content.ReadFromJsonAsync<TwoFactorSetupDto>();
            var secret = ExtractSecretFromUri(setup!.AuthenticatorUri);

            var code = ComputeTotp(secret, DateTimeOffset.UtcNow);
            using var enableResp = await client.PostAsJsonAsync(
                "/api/platform/auth/2fa/enable",
                new EnableTwoFactorCommand(code));
            Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);

            // 4. After MFA enrollment, privileged endpoint succeeds
            using var allowedStoreCreate = await client.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Allowed Shop", $"allowed-{suffix}", StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Created, allowedStoreCreate.StatusCode);
            var createdStore = await allowedStoreCreate.Content.ReadFromJsonAsync<PlatformStoreDto>();
            if (createdStore is not null)
            {
                await _fixture.DeleteStoresAsync(createdStore.Id);
            }
        }
        finally
        {
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
        }
    }
}
