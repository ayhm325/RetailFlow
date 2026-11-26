using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Web;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PlatformAdminMfaHardeningTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public PlatformAdminMfaHardeningTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private WebApplicationFactory<Program> CreateFactory(
        string environmentName,
        Dictionary<string, string?>? overrides = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(environmentName);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var values = new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["DataProtection:RequireCertificateInProduction"] = "false",
                        ["Email:RequireConfiguredInProduction"] = "false"
                    };

                    if (overrides is not null)
                    {
                        foreach (var kvp in overrides)
                        {
                            values[kvp.Key] = kvp.Value;
                        }
                    }

                    config.AddInMemoryCollection(values);
                });
            });
    }

    private async Task<(Guid AdminId, string Email)> CreatePlatformAdminAsync(
        WebApplicationFactory<Program> factory,
        string password,
        bool enableMfa = false)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var adminId = Guid.NewGuid();
        var email = $"platmfa-{suffix}@retailflow.test";

        await using var scope = factory.Services.CreateAsyncScope();
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
            Email = email,
            DisplayName = "Platform Admin Test",
            StoreId = null,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };

        Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
        Assert.True((await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin)).Succeeded);

        if (enableMfa)
        {
            var rawSecret = TotpCryptoHelper.GenerateBase32Secret();
            Assert.True((await userManager.SetAuthenticationTokenAsync(admin, "[AspNetUserStore]", "AuthenticatorKey", rawSecret)).Succeeded);
            Assert.True((await userManager.SetTwoFactorEnabledAsync(admin, true)).Succeeded);
            admin.MfaEnrollmentStatus = MfaEnrollmentStatus.Enabled;
            Assert.True((await userManager.UpdateAsync(admin)).Succeeded);
        }

        return (adminId, email);
    }

    private async Task CleanupUserAsync(Guid userId)
    {
        await using var db = _fixture.CreateDbContext();
        await db.SessionTickets.Where(t => t.UserId == userId).ExecuteDeleteAsync();
        await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
    }

    private static string ExtractSecretFromUri(string authenticatorUri)
    {
        var uri = new Uri(authenticatorUri);
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query["secret"] ?? throw new InvalidOperationException("Missing secret in authenticator URI.");
    }

    private static string ComputeTotp(string base32Secret, DateTimeOffset timestamp)
    {
        var secretBytes = Base32Decode(base32Secret);
        var counter = timestamp.ToUnixTimeSeconds() / 30;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counterBytes);
        }

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes);
        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);
        return (binaryCode % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = input.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var bits = 0;
        var value = 0;
        var output = new List<byte>();

        foreach (var ch in cleaned)
        {
            var idx = alphabet.IndexOf(ch);
            if (idx < 0) continue;
            value = (value << 5) | idx;
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }

        return [.. output];
    }

    [Fact]
    public async Task TestA_ProductionWithoutRequireAdminMfa_EnforcesMandatoryMfa()
    {
        await using var factory = CreateFactory(Environments.Production);
        var (adminId, email) = await CreatePlatformAdminAsync(factory, "ProdMfaPassphrase!123");

        try
        {
            var opts = factory.Services.GetRequiredService<IOptions<PlatformSecurityOptions>>().Value;
            Assert.True(opts.RequireAdminMfa);

            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://retail.example.com"),
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var loginResp = await client.PostAsJsonAsync(
                "/api/platform/auth/login",
                new LoginCommand(email, "ProdMfaPassphrase!123"));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);
            var dto = await loginResp.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            Assert.NotNull(dto);
            Assert.True(dto!.MfaRequiredForPlatformAccess);

            using var blocked = await client.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Blocked", $"blk-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        }
        finally
        {
            await CleanupUserAsync(adminId);
        }
    }

    [Fact]
    public void TestB_ProductionWithExplicitRequireAdminMfaFalse_FailsStartupSafely()
    {
        using var factory = CreateFactory(
            Environments.Production,
            new Dictionary<string, string?> { ["PlatformSecurity:RequireAdminMfa"] = "false" });

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("PlatformSecurity:RequireAdminMfa", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestC_DevelopmentBehavior_DefaultsTrueAndAllowsExplicitDevOverride()
    {
        await using (var devDefault = CreateFactory(Environments.Development))
        {
            using var _ = devDefault.CreateClient();
            Assert.True(devDefault.Services.GetRequiredService<IOptions<PlatformSecurityOptions>>().Value.RequireAdminMfa);
        }

        await using (var devOverride = CreateFactory(
            Environments.Development,
            new Dictionary<string, string?> { ["PlatformSecurity:RequireAdminMfa"] = "false" }))
        {
            using var _ = devOverride.CreateClient();
            Assert.False(devOverride.Services.GetRequiredService<IOptions<PlatformSecurityOptions>>().Value.RequireAdminMfa);
        }
    }

    [Fact]
    public async Task TestD_E_F_G_PlatformAdminMfa_Login_Totp_Recovery_Replay_And_ConcurrentRecovery()
    {
        await using var factory = CreateFactory(Environments.Production);
        const string password = "StrongAdmin!2026";
        var (adminId, email) = await CreatePlatformAdminAsync(factory, password);
        Guid? createdStoreId = null;

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://retail.example.com"),
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            // 1. Enrollment flow
            using var initialLogin = await client.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password));
            Assert.Equal(HttpStatusCode.OK, initialLogin.StatusCode);
            Assert.True((await initialLogin.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.MfaRequiredForPlatformAccess);

            using var setupResp = await client.PostAsJsonAsync("/api/platform/auth/2fa/setup", new BeginTwoFactorSetupCommand(password));
            Assert.Equal(HttpStatusCode.OK, setupResp.StatusCode);
            var setup = await setupResp.Content.ReadFromJsonAsync<TwoFactorSetupDto>();
            var secret = ExtractSecretFromUri(setup!.AuthenticatorUri);

            // Before enable confirmation, privileged endpoint is still forbidden
            using var preEnableStore = await client.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("PreEnable", $"pre-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Forbidden, preEnableStore.StatusCode);

            var code1 = ComputeTotp(secret, DateTimeOffset.UtcNow);
            using var enableResp = await client.PostAsJsonAsync("/api/platform/auth/2fa/enable", new EnableTwoFactorCommand(code1));
            Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);
            var recovery = await enableResp.Content.ReadFromJsonAsync<TwoFactorRecoveryCodesDto>();
            Assert.NotNull(recovery);
            Assert.Equal(10, recovery!.RecoveryCodes.Count);

            // Logout and test Test D: password-only login for MFA-enrolled PlatformAdmin is rejected (401, no cookie)
            await client.PostAsync("/api/platform/auth/logout", null);

            using var pwOnlyClient = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://retail.example.com"),
                HandleCookies = true
            });
            pwOnlyClient.DefaultRequestHeaders.Add("X-CSRF", "1");

            using var pwOnlyLogin = await pwOnlyClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password));
            Assert.Equal(HttpStatusCode.Unauthorized, pwOnlyLogin.StatusCode);
            Assert.False(pwOnlyLogin.Headers.Contains("Set-Cookie"));

            using var blockedAfterPwOnly = await pwOnlyClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Blocked2", $"blk2-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Unauthorized, blockedAfterPwOnly.StatusCode);

            // Test F: Invalid TOTP & replayed same-timestep TOTP are rejected
            using var invalidTotp = await pwOnlyClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password, TwoFactorCode: "000000"));
            Assert.Equal(HttpStatusCode.Unauthorized, invalidTotp.StatusCode);

            using var replayedTotp = await pwOnlyClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password, TwoFactorCode: code1));
            Assert.Equal(HttpStatusCode.Unauthorized, replayedTotp.StatusCode);

            // Test E & G: Concurrent redemption of the same recovery code -> at most one succeeds!
            var recoveryCode = recovery.RecoveryCodes[0];
            using var c1 = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://retail.example.com"), HandleCookies = true });
            using var c2 = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://retail.example.com"), HandleCookies = true });
            c1.DefaultRequestHeaders.Add("X-CSRF", "1");
            c2.DefaultRequestHeaders.Add("X-CSRF", "1");

            var results = await Task.WhenAll(
                c1.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password, RecoveryCode: recoveryCode)),
                c2.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password, RecoveryCode: recoveryCode)));

            var successCount = results.Count(r => r.StatusCode == HttpStatusCode.OK);
            var rejectedCount = results.Count(r => r.StatusCode == HttpStatusCode.Unauthorized);
            Assert.Equal(1, successCount);
            Assert.Equal(1, rejectedCount);

            var winningClient = results[0].StatusCode == HttpStatusCode.OK ? c1 : c2;
            var storeSlug = $"mfaok-{Guid.NewGuid():N}"[..14];
            using var createStore = await winningClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("MFA Verified Store", storeSlug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Created, createStore.StatusCode);
            var createdStore = await createStore.Content.ReadFromJsonAsync<PlatformStoreDto>();
            createdStoreId = createdStore?.Id;

            // Reusing the same recovery code again fails
            using var reusedRecovery = await pwOnlyClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password, RecoveryCode: recoveryCode));
            Assert.Equal(HttpStatusCode.Unauthorized, reusedRecovery.StatusCode);
        }
        finally
        {
            if (createdStoreId.HasValue)
            {
                await _fixture.DeleteStoresAsync(createdStoreId.Value);
            }
            await CleanupUserAsync(adminId);
        }
    }

    [Fact]
    public async Task TestH_J_K_AuthorizationBypass_MfaDisabledInDb_And_PrivilegeElevation_AreDenied()
    {
        await using var factory = CreateFactory(Environments.Production);
        const string password = "StrongAdmin!2026";
        var (adminId, email) = await CreatePlatformAdminAsync(factory, password);

        // Also create a Customer in a store to test role isolation & privilege elevation (Test H & K)
        var storeSlug = $"elev-{Guid.NewGuid():N}"[..12];
        PlatformStoreDto store;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var platformService = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
            store = await platformService.CreateStoreAsync(
                new CreateStoreCommand("Elevation Test Store", storeSlug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false),
                CancellationToken.None);
            Assert.True(await platformService.ApproveStoreAsync(store.Id, CancellationToken.None));
        }
        Guid customerId = Guid.Empty;

        try
        {
            using var anonClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://retail.example.com"), HandleCookies = false });
            anonClient.DefaultRequestHeaders.Add("X-CSRF", "1");
            anonClient.DefaultRequestHeaders.Add("amr", "mfa");
            anonClient.DefaultRequestHeaders.Add("mfa_enrolled", "true");

            // Anonymous with forged headers -> 401
            using var anonResp = await anonClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Forge", $"frg-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Unauthorized, anonResp.StatusCode);

            // Test I & K: Customer registration/login unaffected, and elevating Customer to PlatformAdmin without MFA still blocks /api/platform/stores
            using var custClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://retail.example.com"), HandleCookies = true });
            custClient.DefaultRequestHeaders.Add("X-CSRF", "1");
            var custEmail = $"cust-{Guid.NewGuid():N}@retailflow.test";
            using var regResp = await custClient.PostAsJsonAsync(
                $"/api/stores/{storeSlug}/auth/register",
                new RegisterCustomerCommand(custEmail, "Cust One", "CustPassphrase!123"));
            Assert.Equal(HttpStatusCode.Created, regResp.StatusCode);
            customerId = (await regResp.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId;

            using var custBlocked = await custClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("CustBlock", $"cb-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Forbidden, custBlocked.StatusCode);

            // Elevate customer to PlatformAdmin in DB without MFA -> existing session & new login still blocked from /api/platform/stores
            await using (var db = _fixture.CreateDbContext())
            {
                var dbCust = await db.Users.SingleAsync(u => u.Id == customerId);
                dbCust.StoreId = null;
                await db.SaveChangesAsync();
            }

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<RetailFlowIdentityUser>>();
                var u = await userManager.FindByIdAsync(customerId.ToString());
                Assert.NotNull(u);
                Assert.True((await userManager.AddToRoleAsync(u!, IdentityRoleNames.PlatformAdmin)).Succeeded);
            }

            using var elevatedBlocked = await custClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("ElevBlock", $"eb-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.True(elevatedBlocked.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

            // Test H & J: Enroll PlatformAdmin, then disable TwoFactorEnabled in DB -> existing MFA cookie is rejected by RetailFlowCookieEvents!
            using var adminClient = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://retail.example.com"), HandleCookies = true });
            adminClient.DefaultRequestHeaders.Add("X-CSRF", "1");
            await adminClient.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password));
            var setupResp = await adminClient.PostAsJsonAsync("/api/platform/auth/2fa/setup", new BeginTwoFactorSetupCommand(password));
            var setup = await setupResp.Content.ReadFromJsonAsync<TwoFactorSetupDto>();
            var code = ComputeTotp(ExtractSecretFromUri(setup!.AuthenticatorUri), DateTimeOffset.UtcNow);
            var enableResp = await adminClient.PostAsJsonAsync("/api/platform/auth/2fa/enable", new EnableTwoFactorCommand(code));
            Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);

            // Simulate unexpected MFA disable in DB without clearing cookie
            await using (var db = _fixture.CreateDbContext())
            {
                var dbAdmin = await db.Users.SingleAsync(u => u.Id == adminId);
                dbAdmin.TwoFactorEnabled = false;
                dbAdmin.MfaEnrollmentStatus = MfaEnrollmentStatus.NotEnrolled;
                await db.SaveChangesAsync();
            }

            using var afterDbDisable = await adminClient.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("AfterDisable", $"ad-{Guid.NewGuid():N}"[..12], StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Unauthorized, afterDbDisable.StatusCode);
        }
        finally
        {
            await CleanupUserAsync(adminId);
            if (customerId != Guid.Empty)
            {
                await CleanupUserAsync(customerId);
            }
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }
}
