using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

public sealed class ReverseProxyForwardedHeadersIntegrationTests
    : IClassFixture<SessionTransportApiFactory>
{
    private const string PublicOrigin = "https://retail.example.com";
    private const string PublicHost = "retail.example.com";
    private const string TrustedProxyIp = "10.20.30.40";
    private const string UntrustedProxyIp = "198.51.100.77";
    private const string TestPassword = "RetailFlow!TestPassword123";

    private readonly SessionTransportApiFactory _baseFactory;

    public ReverseProxyForwardedHeadersIntegrationTests(
        SessionTransportApiFactory baseFactory)
    {
        _baseFactory = baseFactory;
    }

    [Fact]
    public void ReverseProxyEnabled_WithoutKnownProxies_FailsStartup()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ReverseProxy:Enabled"] = "true"
                });
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("KnownProxies", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReverseProxyEnabled_WithInvalidKnownProxyIp_FailsStartup()
    {
        using var factory = _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ReverseProxy:Enabled"] = "true",
                    ["ReverseProxy:KnownProxies:0"] = "not-an-ip"
                });
            });
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("not-an-ip", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TrustedProxy_WithXForwardedProtoHttps_CompletesLoginMeAndLogout_WithoutRedirectLoop()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var tenant = await CreateTenantAsync("proxy-trusted");
            tenants.Add(tenant);

            using var factory = CreateReverseProxyFactory(
                IPAddress.Parse(TrustedProxyIp));

            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri($"http://{PublicHost}"),
                    AllowAutoRedirect = false,
                    HandleCookies = false
                });

            var basePath = $"/api/stores/{tenant.Slug}/customers";

            // 1. Login over internal HTTP with trusted X-Forwarded-Proto: https.
            using var loginRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{basePath}/login")
            {
                Content = JsonContent.Create(new
                {
                    Email = tenant.Email,
                    Password = TestPassword
                })
            };

            loginRequest.Headers.Host = PublicHost;
            loginRequest.Headers.Add("X-Forwarded-Proto", "https");
            loginRequest.Headers.Add("X-Forwarded-For", "203.0.113.10");
            loginRequest.Headers.Add("Origin", PublicOrigin);
            loginRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
            loginRequest.Headers.Add("X-RetailFlow-Request", "1");

            using var loginResponse = await client.SendAsync(loginRequest);

            Assert.Equal(HttpStatusCode.NoContent, loginResponse.StatusCode);

            var setCookieHeaders = loginResponse.Headers
                .GetValues("Set-Cookie")
                .ToArray();

            var expectedCookieName =
                $"__Secure-RetailFlow.StoreCustomer.store.{tenant.StoreId:N}";

            var issuedCookie = Assert.Single(
                setCookieHeaders,
                h => h.StartsWith($"{expectedCookieName}=", StringComparison.Ordinal));

            Assert.Contains($"path={basePath}", issuedCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", issuedCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", issuedCookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=lax", issuedCookie, StringComparison.OrdinalIgnoreCase);

            var cookiePair = issuedCookie.Split(';', 2)[0];

            // 2. GET /me through trusted proxy preserves path and returns 200 OK.
            using var meRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{basePath}/me");

            meRequest.Headers.Host = PublicHost;
            meRequest.Headers.Add("X-Forwarded-Proto", "https");
            meRequest.Headers.Add("Cookie", cookiePair);

            using var meResponse = await client.SendAsync(meRequest);

            Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);

            // 3. Unknown API route returns 404 (not SPA HTML fallback).
            using var notFoundRequest = new HttpRequestMessage(
                HttpMethod.Get,
                $"{basePath}/does-not-exist");

            notFoundRequest.Headers.Host = PublicHost;
            notFoundRequest.Headers.Add("X-Forwarded-Proto", "https");

            using var notFoundResponse = await client.SendAsync(notFoundRequest);

            Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);

            // 4. Logout through trusted proxy deletes cookie and revokes session.
            using var logoutRequest = new HttpRequestMessage(
                HttpMethod.Post,
                $"{basePath}/logout");

            logoutRequest.Headers.Host = PublicHost;
            logoutRequest.Headers.Add("X-Forwarded-Proto", "https");
            logoutRequest.Headers.Add("Origin", PublicOrigin);
            logoutRequest.Headers.Add("Sec-Fetch-Site", "same-origin");
            logoutRequest.Headers.Add("X-RetailFlow-Request", "1");
            logoutRequest.Headers.Add("Cookie", cookiePair);

            using var logoutResponse = await client.SendAsync(logoutRequest);

            Assert.Equal(HttpStatusCode.NoContent, logoutResponse.StatusCode);
            Assert.Equal(0, await CountSessionsAsync(tenant.StoreId));
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task UntrustedProxy_XForwardedProtoHttps_IsIgnored_AndTriggersHttpsRedirect()
    {
        using var factory = CreateReverseProxyFactory(
            IPAddress.Parse(UntrustedProxyIp));

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri($"http://{PublicHost}"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/stores/unknown/customers/me");

        request.Headers.Host = PublicHost;
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal("https", response.Headers.Location!.Scheme);
        Assert.Equal(PublicHost, response.Headers.Location.Host);
    }

    [Fact]
    public async Task DisallowedHost_IsRejected_WithBadRequest()
    {
        using var factory = CreateReverseProxyFactory(
            IPAddress.Parse(TrustedProxyIp));

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri($"http://{PublicHost}"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/api/stores/unknown/customers/me");

        request.Headers.Host = "evil.example.com";
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private WebApplicationFactory<SessionTransportProgramMarker> CreateReverseProxyFactory(
        IPAddress remoteIpAddress)
    {
        var keysDir = Path.Combine(
            Path.GetTempPath(),
            "RetailFlow.ReverseProxyTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(keysDir);

        return _baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AllowedHosts"] = PublicHost,
                    ["Security:BrowserCsrf:BrowserOrigin"] = PublicOrigin,
                    ["ReverseProxy:Enabled"] = "true",
                    ["ReverseProxy:KnownProxies:0"] = TrustedProxyIp,
                    ["DataProtection:KeysDirectory"] = keysDir
                });
            });

            builder.ConfigureTestServices(services =>
            {
                services.Configure<HttpsRedirectionOptions>(options =>
                {
                    options.HttpsPort = 443;
                });

                services.AddSingleton<IStartupFilter>(
                    new RemoteIpStartupFilter(remoteIpAddress));
            });
        });
    }

    private async Task<TestTenant> CreateTenantAsync(string prefix)
    {
        await using var scope = _baseFactory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var identityDb = services.GetRequiredService<IdentitySpikeDbContext>();
        var accessor = services.GetRequiredService<IIdentityLookupScopeAccessor>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        var storeId = Guid.NewGuid();
        var slug = $"{prefix}-{storeId:N}";
        var email = $"{prefix}-{storeId:N}@retailflow.test";

        identityDb.Stores.Add(new SpikeStore
        {
            Id = storeId,
            Name = $"Store {prefix}",
            Slug = slug,
            IsActive = true
        });

        await identityDb.SaveChangesAsync();

        accessor.SetCurrent(IdentityLookupScope.ForStoreCustomer(storeId));

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid().ToString("D"),
            UserName = $"cust_{storeId:N}",
            Email = email,
            EmailConfirmed = true,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId
        };

        var result = await userManager.CreateAsync(user, TestPassword);
        Assert.True(result.Succeeded);

        return new TestTenant(storeId, slug, user.Id, email);
    }

    private async Task<int> CountSessionsAsync(Guid storeId)
    {
        await using var scope = _baseFactory.Services.CreateAsyncScope();
        var dbFactory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<SessionTicketDbContext>>();

        await using var db = await dbFactory.CreateDbContextAsync();

        return await db.AuthenticationTickets
            .CountAsync(t => t.StoreId == storeId);
    }

    private async Task RemoveTestDataAsync(IEnumerable<TestTenant> tenants)
    {
        var tenantList = tenants.ToList();
        if (tenantList.Count == 0)
        {
            return;
        }

        var userIds = tenantList.Select(t => t.UserId).ToArray();
        var storeIds = tenantList.Select(t => t.StoreId).ToArray();

        await using var scope = _baseFactory.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var ticketFactory = services
            .GetRequiredService<IDbContextFactory<SessionTicketDbContext>>();
        await using var ticketDb = await ticketFactory.CreateDbContextAsync();

        await ticketDb.AuthenticationTickets
            .Where(t => userIds.Contains(t.UserId) || storeIds.Contains(t.StoreId))
            .ExecuteDeleteAsync();

        var identityDb = services.GetRequiredService<IdentitySpikeDbContext>();

        var users = await identityDb.Users
            .Where(u => userIds.Contains(u.Id))
            .ToListAsync();
        identityDb.Users.RemoveRange(users);
        await identityDb.SaveChangesAsync();

        var stores = await identityDb.Stores
            .Where(s => storeIds.Contains(s.Id))
            .ToListAsync();
        identityDb.Stores.RemoveRange(stores);
        await identityDb.SaveChangesAsync();
    }

    private sealed record TestTenant(
        Guid StoreId,
        string Slug,
        string UserId,
        string Email);

    private sealed class RemoteIpStartupFilter : IStartupFilter
    {
        private readonly IPAddress _remoteIpAddress;

        public RemoteIpStartupFilter(IPAddress remoteIpAddress)
        {
            _remoteIpAddress = remoteIpAddress;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, nextMiddleware) =>
                {
                    context.Connection.RemoteIpAddress = _remoteIpAddress;
                    await nextMiddleware();
                });

                next(app);
            };
        }
    }
}
