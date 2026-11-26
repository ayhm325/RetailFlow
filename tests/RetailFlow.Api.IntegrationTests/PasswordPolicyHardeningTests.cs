using System.Net;
using System.Net.Http.Json;
using System.Text;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;
using RetailFlow.Domain.Identity;
using RetailFlow.Domain.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PasswordPolicyHardeningTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public PasswordPolicyHardeningTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class CapturingEmailSender : IRetailFlowEmailSender
    {
        public string? LastResetLink { get; private set; }

        public Task SendPasswordResetAsync(string toEmail, string resetLink, CancellationToken cancellationToken)
        {
            LastResetLink = resetLink;
            return Task.CompletedTask;
        }

        public Task SendEmailConfirmationAsync(string toEmail, string confirmationLink, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SendSecurityNotificationAsync(string toEmail, string subject, string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private WebApplicationFactory<Program> CreateFactory(CapturingEmailSender? emailSender = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "localhost",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["Email:PublicBaseUrl"] = "https://shop.retailflow.test",
                        ["PlatformSecurity:RequireAdminMfa"] = "false"
                    });
                });

                if (emailSender is not null)
                {
                    builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IRetailFlowEmailSender>();
                        services.AddSingleton<IRetailFlowEmailSender>(emailSender);
                    });
                }
            });
    }

    private async Task<PlatformStoreDto> CreateApprovedStoreAsync(WebApplicationFactory<Program> factory, string slug)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
        var store = await platform.CreateStoreAsync(
            new CreateStoreCommand("PW Policy Store", slug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false),
            CancellationToken.None);
        Assert.True(await platform.ApproveStoreAsync(store.Id, CancellationToken.None));
        return store;
    }

    private async Task CleanupUsersAsync(params Guid[] userIds)
    {
        await using var db = _fixture.CreateDbContext();
        await db.SessionTickets.Where(t => userIds.Contains(t.UserId)).ExecuteDeleteAsync();
        await db.Users.Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task TestA_MinimumLength15_EnforcedAcrossRegistrationProvisioningAndReset()
    {
        var emailSender = new CapturingEmailSender();
        await using var factory = CreateFactory(emailSender);
        var slug = $"pwmin-{Guid.NewGuid():N}"[..13];
        var store = await CreateApprovedStoreAsync(factory, slug);
        var createdUserIds = new List<Guid>();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            // 14 chars -> rejected on customer registration
            using var reg14 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand($"u14-{slug}@test.local", "User 14", "FourteenChar12"));
            Assert.False(reg14.IsSuccessStatusCode);

            // Exactly 15 chars -> accepted
            var custEmail = $"u15-{slug}@test.local";
            using var reg15 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(custEmail, "User 15", "FifteenCharPass"));
            Assert.Equal(HttpStatusCode.Created, reg15.StatusCode);
            var custDto = await reg15.Content.ReadFromJsonAsync<AuthenticatedUserDto>();
            createdUserIds.Add(custDto!.UserId);

            // Merchant owner provisioning rejects < 15 chars and accepts >= 15 chars
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var authService = scope.ServiceProvider.GetRequiredService<IAuthService>();
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    authService.CreateMerchantOwnerAsync(
                        store.Id,
                        new CreateMerchantUserCommand($"m14-{slug}@test.local", "Merch 14", "FourteenChar12"),
                        CancellationToken.None));

                var merchDto = await authService.CreateMerchantOwnerAsync(
                    store.Id,
                    new CreateMerchantUserCommand($"m15-{slug}@test.local", "Merch 15", "MerchantFifteen"),
                    CancellationToken.None);
                createdUserIds.Add(merchDto.UserId);
            }

            // Password reset rejects 14 chars and accepts >= 15 chars
            await client.PostAsJsonAsync($"/api/stores/{slug}/auth/forgot-password", new ForgotPasswordCommand(custEmail));
            Assert.NotNull(emailSender.LastResetLink);
            var token = System.Web.HttpUtility.ParseQueryString(new Uri(emailSender.LastResetLink!).Query)["token"]!;

            using var reset14 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/reset-password",
                new ResetPasswordCommand(custEmail, token, "FourteenChar12"));
            Assert.Equal(HttpStatusCode.BadRequest, reset14.StatusCode);

            using var reset15 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/reset-password",
                new ResetPasswordCommand(custEmail, token, "UpdatedFifteenP"));
            Assert.Equal(HttpStatusCode.NoContent, reset15.StatusCode);
        }
        finally
        {
            await CleanupUsersAsync([.. createdUserIds]);
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestB_NoArbitraryCompositionRules_AcceptsLowercaseDigitsAndSpaces()
    {
        await using var factory = CreateFactory();
        var slug = $"pwcomp-{Guid.NewGuid():N}"[..14];
        var store = await CreateApprovedStoreAsync(factory, slug);
        var createdUserIds = new List<Guid>();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            var passwords = new[]
            {
                "onlylowercasepassphrase",
                "8492017463829104",
                "sunlit cedar river stone"
            };

            for (var i = 0; i < passwords.Length; i++)
            {
                var email = $"comp{i}-{slug}@test.local";
                using var reg = await client.PostAsJsonAsync(
                    $"/api/stores/{slug}/auth/register",
                    new RegisterCustomerCommand(email, $"User {i}", passwords[i]));
                Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
                createdUserIds.Add((await reg.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId);

                using var login = await client.PostAsJsonAsync(
                    $"/api/stores/{slug}/auth/login",
                    new LoginCommand(email, passwords[i]));
                Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            }
        }
        finally
        {
            await CleanupUsersAsync([.. createdUserIds]);
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestC_D_UnicodeArabicNfc_And_MaxLength1024WithoutTruncation()
    {
        await using var factory = CreateFactory();
        var slug = $"pwuni-{Guid.NewGuid():N}"[..13];
        var store = await CreateApprovedStoreAsync(factory, slug);
        var createdUserIds = new List<Guid>();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            // Arabic passphrase
            var arabicEmail = $"ar-{slug}@test.local";
            const string arabicPass = "كلمة مرور عربية قوية جداً 2026";
            using var regAr = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(arabicEmail, "Arabic User", arabicPass));
            Assert.Equal(HttpStatusCode.Created, regAr.StatusCode);
            createdUserIds.Add((await regAr.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId);

            using var loginAr = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(arabicEmail, arabicPass));
            Assert.Equal(HttpStatusCode.OK, loginAr.StatusCode);

            // NFD register -> NFC login consistency
            var uniEmail = $"uni-{slug}@test.local";
            var decomposed = "cafe\u0301 au lait maison nord".Normalize(NormalizationForm.FormD);
            var composed = decomposed.Normalize(NormalizationForm.FormC);
            Assert.NotEqual(decomposed, composed);

            using var regUni = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(uniEmail, "Unicode User", decomposed));
            Assert.Equal(HttpStatusCode.Created, regUni.StatusCode);
            createdUserIds.Add((await regUni.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId);

            using var loginUni = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(uniEmail, composed));
            Assert.Equal(HttpStatusCode.OK, loginUni.StatusCode);

            // Max length 64 and 1024 without truncation, 1025 rejected
            var pass64 = new string('x', 63) + "A";
            var pass64WrongTail = new string('x', 63) + "B";
            var email64 = $"p64-{slug}@test.local";
            using var reg64 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(email64, "User 64", pass64));
            Assert.Equal(HttpStatusCode.Created, reg64.StatusCode);
            createdUserIds.Add((await reg64.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId);

            using var login64Wrong = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(email64, pass64WrongTail));
            Assert.Equal(HttpStatusCode.Unauthorized, login64Wrong.StatusCode);

            var pass1024 = string.Concat(Enumerable.Repeat("AbCd1234", 128));
            Assert.Equal(1024, pass1024.Length);
            var email1024 = $"p1024-{slug}@test.local";
            using var reg1024 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(email1024, "User 1024", pass1024));
            Assert.Equal(HttpStatusCode.Created, reg1024.StatusCode);
            createdUserIds.Add((await reg1024.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId);

            using var reg1025 = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand($"p1025-{slug}@test.local", "User 1025", pass1024 + "Z"));
            Assert.False(reg1025.IsSuccessStatusCode);
        }
        finally
        {
            await CleanupUsersAsync([.. createdUserIds]);
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }

    [Fact]
    public async Task TestE_F_G_Blocklist_NoSubstringFalsePositive_LegacyCompatibility_And_Lockout()
    {
        await using var factory = CreateFactory();
        var slug = $"pwblk-{Guid.NewGuid():N}"[..13];
        var store = await CreateApprovedStoreAsync(factory, slug);
        var createdUserIds = new List<Guid>();

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            // Blocked common/product/predictable/account passwords (>=15 chars)
            var blocked = new[]
            {
                "passwordpassword",
                "123456789012345",
                "retailflow12345",
                "aaaaaaaaaaaaaaa",
                $"blocked-{slug}@test.local"
            };

            foreach (var bad in blocked)
            {
                using var resp = await client.PostAsJsonAsync(
                    $"/api/stores/{slug}/auth/register",
                    new RegisterCustomerCommand($"blocked-{slug}@test.local", "Blocked User Name", bad));
                Assert.False(resp.IsSuccessStatusCode);
            }

            // Strong passphrase containing "retailflow" as an ordinary word is ACCEPTED (no substring false positive)
            var okEmail = $"ok-{slug}@test.local";
            const string strongWithWord = "blue mountain retailflow horizon";
            using var regOk = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand(okEmail, "Valid User", strongWithWord));
            Assert.Equal(HttpStatusCode.Created, regOk.StatusCode);
            createdUserIds.Add((await regOk.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId);

            // Online guessing protection: 5 failed attempts -> 423 Locked
            for (var attempt = 0; attempt < 5; attempt++)
            {
                using var fail = await client.PostAsJsonAsync(
                    $"/api/stores/{slug}/auth/login",
                    new LoginCommand(okEmail, "wrong-passphrase-attempt"));
                Assert.True(fail.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Locked);
            }

            using var lockedResp = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(okEmail, strongWithWord));
            Assert.Equal(HttpStatusCode.Locked, lockedResp.StatusCode);

            // Legacy short password (<15 chars) already persisted in DB still authenticates cleanly
            var legacyId = Guid.NewGuid();
            var legacyEmail = $"legacy-{slug}@test.local";
            await using (var db = _fixture.CreateDbContext())
            {
                var hasher = new RetailFlowPasswordHasher();
                var legacyUser = new RetailFlowIdentityUser
                {
                    Id = legacyId,
                    UserName = legacyId.ToString("N"),
                    NormalizedUserName = legacyId.ToString("N").ToUpperInvariant(),
                    Email = legacyEmail,
                    NormalizedEmail = legacyEmail.ToUpperInvariant(),
                    DisplayName = "Legacy Short User",
                    StoreId = store.Id,
                    IsActive = true,
                    SecurityStamp = Guid.NewGuid().ToString("N"),
                    ConcurrencyStamp = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                legacyUser.PasswordHash = hasher.HashPassword(legacyUser, "Short8!!");
                db.Users.Add(legacyUser);
                db.UserRoles.Add(new IdentityUserRole<Guid>
                {
                    UserId = legacyId,
                    RoleId = RetailFlowIdentityRole.CustomerRoleId
                });
                await db.SaveChangesAsync();
                createdUserIds.Add(legacyId);
            }

            using var legacyLogin = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/login",
                new LoginCommand(legacyEmail, "Short8!!"));
            Assert.Equal(HttpStatusCode.OK, legacyLogin.StatusCode);
        }
        finally
        {
            await CleanupUsersAsync([.. createdUserIds]);
            await _fixture.DeleteStoresAsync(store.Id);
        }
    }
}
