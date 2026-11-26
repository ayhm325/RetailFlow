using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
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

public sealed class SessionLifetimeHardeningTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public SessionLifetimeHardeningTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class AdjustableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public AdjustableTimeProvider(DateTimeOffset initialUtcNow)
        {
            _utcNow = initialUtcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private WebApplicationFactory<Program> CreateFactory(TimeProvider timeProvider)
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
                        ["PlatformSecurity:RequireAdminMfa"] = "false"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton(timeProvider);
                });
            });
    }

    private async Task<(Guid StoreId, string StoreSlug)> SetupStoreAsync(WebApplicationFactory<Program> factory)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var slug = $"sess-{suffix}";
        await using var scope = factory.Services.CreateAsyncScope();
        var platform = scope.ServiceProvider.GetRequiredService<IPlatformStoreService>();
        var store = await platform.CreateStoreAsync(
            new CreateStoreCommand("Session Store", slug, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false),
            CancellationToken.None);
        Assert.True(await platform.ApproveStoreAsync(store.Id, CancellationToken.None));
        return (store.Id, slug);
    }

    [Fact]
    public async Task TestA_CookieIdleExpiration_RejectsAfter12HoursInactivity()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var factory = CreateFactory(clock);
        var (storeId, slug) = await SetupStoreAsync(factory);
        Guid userId = Guid.Empty;

        try
        {
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            client.DefaultRequestHeaders.Add("X-CSRF", "1");

            var reg = await client.PostAsJsonAsync(
                $"/api/stores/{slug}/auth/register",
                new RegisterCustomerCommand($"idle-{slug}@test.local", "Idle User", "IdlePassphrase!2026"));
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
            userId = (await reg.Content.ReadFromJsonAsync<AuthenticatedUserDto>())!.UserId;

            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/stores/{slug}/cart")).StatusCode);

            // Advance beyond Customer idle timeout (12h)
            clock.Advance(TimeSpan.FromHours(12) + TimeSpan.FromSeconds(1));

            var afterIdle = await client.GetAsync($"/api/stores/{slug}/cart");
            Assert.Equal(HttpStatusCode.Unauthorized, afterIdle.StatusCode);
        }
        finally
        {
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == userId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();
            await _fixture.DeleteStoresAsync(storeId);
        }
    }

    [Fact]
    public async Task TestB_C_D_E_O_P_AbsoluteLifetime_CannotBeExtendedBySlidingRenewal_AcrossInstances()
    {
        var clock = new AdjustableTimeProvider(DateTimeOffset.UtcNow);
        await using var instanceA = CreateFactory(clock);
        await using var instanceB = CreateFactory(clock);
        var (storeId, slug) = await SetupStoreAsync(instanceA);
        Guid adminId = Guid.Empty;

        try
        {
            var email = $"abs-{slug}@test.local";
            const string password = "AdminSessionPassphrase!2026";
            await using (var scope = instanceA.Services.CreateAsyncScope())
            {
                var scopeAccessor = scope.ServiceProvider.GetRequiredService<IIdentityLookupScopeAccessor>();
                scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());
                var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<RetailFlow.Infrastructure.Identity.RetailFlowIdentityUser>>();
                var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<RetailFlow.Infrastructure.Identity.RetailFlowIdentityRole>>();

                if (!await roleManager.RoleExistsAsync(IdentityRoleNames.PlatformAdmin))
                {
                    await roleManager.CreateAsync(new RetailFlow.Infrastructure.Identity.RetailFlowIdentityRole
                    {
                        Id = RetailFlow.Infrastructure.Identity.RetailFlowIdentityRole.PlatformAdminRoleId,
                        Name = IdentityRoleNames.PlatformAdmin,
                        NormalizedName = IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                    });
                }

                adminId = Guid.NewGuid();
                var admin = new RetailFlow.Infrastructure.Identity.RetailFlowIdentityUser
                {
                    Id = adminId,
                    UserName = adminId.ToString("N"),
                    Email = email,
                    DisplayName = "Abs Admin",
                    StoreId = null,
                    IsActive = true,
                    CreatedAtUtc = clock.GetUtcNow()
                };
                Assert.True((await userManager.CreateAsync(admin, password)).Succeeded);
                Assert.True((await userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin)).Succeeded);
            }

            // Authenticate on Instance A (PlatformAdmin: 1h idle, 8h absolute)
            var cookies = new CookieContainer();
            using var handlerA = instanceA.Server.CreateHandler();
            using var clientA = instanceA.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = true });
            clientA.DefaultRequestHeaders.Add("X-CSRF", "1");

            var loginResp = await clientA.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password));
            Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

            // Copy session cookie to Instance B client to test multi-instance validation
            using var clientB = instanceB.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), HandleCookies = false });
            clientB.DefaultRequestHeaders.Add("X-CSRF", "1");
            if (loginResp.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                clientB.DefaultRequestHeaders.Add("Cookie", setCookies.Select(c => c.Split(';')[0]));
            }

            await using (var db = _fixture.CreateDbContext())
            {
                var ticket = await db.SessionTickets.AsNoTracking().SingleAsync(t => t.UserId == adminId);
                Assert.Equal(ticket.CreatedAtUtc.AddHours(8), ticket.AbsoluteExpiresAtUtc);
                Assert.Equal(ticket.CreatedAtUtc.AddHours(1), ticket.ExpiresAtUtc);
            }

            // Continuous activity every 45m up to 7h30m (triggers multiple sliding renewals, never exceeding 8h)
            for (var step = 0; step < 10; step++)
            {
                clock.Advance(TimeSpan.FromMinutes(45)); // total at step 9 = 450m = 7h30m
                var res = await clientA.PostAsJsonAsync(
                    "/api/platform/stores",
                    new CreateStoreCommand("Invalid", string.Empty, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
                // Authenticated past authZ (StoreCommand fails validation 400/500 or succeeds, NOT 401)
                Assert.NotEqual(HttpStatusCode.Unauthorized, res.StatusCode);
            }

            // At 7h59m59s (1 second before 8h absolute deadline), still accepted
            clock.Advance(TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59));
            var beforeDeadline = await clientB.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Invalid", string.Empty, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.NotEqual(HttpStatusCode.Unauthorized, beforeDeadline.StatusCode);

            // Advance 1 second -> exactly at 8h00m00s absolute deadline -> rejected on BOTH Instance A and Instance B!
            clock.Advance(TimeSpan.FromSeconds(1));
            var atDeadlineA = await clientA.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Invalid", string.Empty, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            var atDeadlineB = await clientB.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Invalid", string.Empty, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.Equal(HttpStatusCode.Unauthorized, atDeadlineA.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, atDeadlineB.StatusCode);

            // Reauthentication creates a fresh session with a new 8h absolute deadline
            var relogin = await clientA.PostAsJsonAsync("/api/platform/auth/login", new LoginCommand(email, password));
            Assert.Equal(HttpStatusCode.OK, relogin.StatusCode);
            var afterRelogin = await clientA.PostAsJsonAsync(
                "/api/platform/stores",
                new CreateStoreCommand("Invalid", string.Empty, StoreType.OnlineOnly, "JOD", "Asia/Amman", true, false));
            Assert.NotEqual(HttpStatusCode.Unauthorized, afterRelogin.StatusCode);
        }
        finally
        {
            await using var db = _fixture.CreateDbContext();
            await db.SessionTickets.Where(t => t.UserId == adminId).ExecuteDeleteAsync();
            await db.Users.Where(u => u.Id == adminId).ExecuteDeleteAsync();
            await _fixture.DeleteStoresAsync(storeId);
        }
    }
}
