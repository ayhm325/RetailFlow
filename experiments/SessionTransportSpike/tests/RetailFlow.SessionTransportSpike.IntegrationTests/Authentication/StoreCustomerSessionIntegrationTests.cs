using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.Authentication;
using RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Authentication;

public sealed class StoreCustomerSessionIntegrationTests
    : IClassFixture<SessionTransportApiFactory>
{
    private const string TestPassword =
        "RetailFlow-Integration-2026!";

    private readonly SessionTransportApiFactory _factory;

    public StoreCustomerSessionIntegrationTests(
        SessionTransportApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_Me_Logout_RemovesBrowserSessionCookie()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateClient();

            var tenant = await CreateTenantAsync("session-lifecycle");
            tenants.Add(tenant);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            // 1. Login through the real HTTP endpoint.
            using var loginResponse = await client.PostAsJsonAsync(
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = TestPassword
                });

            Assert.Equal(
                HttpStatusCode.NoContent,
                loginResponse.StatusCode);

            var issuedCookie = ReadIssuedCookie(
                loginResponse,
                tenant.StoreId,
                tenant.Slug);

            // 2. Request the authenticated Me endpoint.
            using var meResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Get,
                $"{basePath}/me",
                issuedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                meResponse.StatusCode);

            var me = await meResponse.Content
                .ReadFromJsonAsync<MeResponse>();

            Assert.NotNull(me);
            Assert.Equal(tenant.UserId, me.UserId);

            Assert.Equal(
                AccountKind.StoreCustomer.ToString(),
                me.AccountKind);

            Assert.Equal(
                tenant.StoreId.ToString("D"),
                me.StoreId);

            // 3. Logout using the same issued cookie.
            using var logoutResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Post,
                $"{basePath}/logout",
                issuedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.NoContent,
                logoutResponse.StatusCode);

            // 4. The deletion cookie must use the same name and path.
            var deletionHeaders = GetSetCookieHeaders(logoutResponse);

            var deletionCookie = Assert.Single(
                deletionHeaders.Where(header =>
                    header.StartsWith(
                        $"{issuedCookie.Name}=",
                        StringComparison.Ordinal)));

            Assert.Contains(
                $"path=/api/stores/{tenant.Slug}/customers",
                deletionCookie,
                StringComparison.OrdinalIgnoreCase);

            Assert.True(
                deletionCookie.Contains(
                    "expires=",
                    StringComparison.OrdinalIgnoreCase) ||
                deletionCookie.Contains(
                    "max-age=0",
                    StringComparison.OrdinalIgnoreCase),
                "Logout must expire the issued browser cookie.");

            // 5. Simulate the browser after applying Set-Cookie deletion:
            // it must no longer send the session cookie.
            using var afterLogoutResponse = await client.GetAsync(
                $"{basePath}/me");

            Assert.Equal(
                HttpStatusCode.Found,
                afterLogoutResponse.StatusCode);

            Assert.NotNull(afterLogoutResponse.Headers.Location);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task StoreACookie_CannotAuthenticateAgainstStoreB()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateClient();

            var storeA = await CreateTenantAsync("session-store-a");
            tenants.Add(storeA);

            var storeB = await CreateTenantAsync("session-store-b");
            tenants.Add(storeB);

            var storeAPath =
                $"/api/stores/{storeA.Slug}/customers";

            var storeBPath =
                $"/api/stores/{storeB.Slug}/customers";

            // Login to Store A.
            using var loginResponse = await client.PostAsJsonAsync(
                $"{storeAPath}/login",
                new
                {
                    Email = storeA.Email,
                    Password = TestPassword
                });

            Assert.Equal(
                HttpStatusCode.NoContent,
                loginResponse.StatusCode);

            var storeACookie = ReadIssuedCookie(
                loginResponse,
                storeA.StoreId,
                storeA.Slug);

            // Case 1: Send Store A cookie under its original name to Store B.
            // CookieManager must look for Store B's cookie name instead.
            using var normalCrossStoreResponse =
                await SendWithCookieAsync(
                    client,
                    HttpMethod.Get,
                    $"{storeBPath}/me",
                    storeACookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Found,
                normalCrossStoreResponse.StatusCode);

            // Case 2: Simulate a stronger attack:
            // rename Store A's valid ticket cookie to Store B's cookie name.
            // The ticket still contains Store A's signed StoreId claim.
            var forgedCookieHeader = RemapCookieName(
                storeACookie.CookieHeader,
                storeACookie.Name,
                BuildCookieName(storeB.StoreId));

            using var forgedResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Get,
                $"{storeBPath}/me",
                forgedCookieHeader);

            // Authentication events must reject the mismatched StoreId claim.
            Assert.Equal(
                HttpStatusCode.Found,
                forgedResponse.StatusCode);

            var forgedResponseCookies =
                GetSetCookieHeaders(forgedResponse);

            // Rejection must delete the cookie selected for Store B,
            // using Store B's path.
            var rejectedCookie = Assert.Single(
                forgedResponseCookies.Where(header =>
                    header.StartsWith(
                        $"{BuildCookieName(storeB.StoreId)}=",
                        StringComparison.Ordinal)));

            Assert.Contains(
                $"path=/api/stores/{storeB.Slug}/customers",
                rejectedCookie,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    private HttpClient CreateClient()
    {
        return _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false
            });
    }

    private async Task<TestTenant> CreateTenantAsync(string prefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        var store = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = $"{prefix} Integration Test",
            Slug = $"{prefix}-{uniqueId}".ToLowerInvariant(),
            IsActive = true
        };

        var tenant = new TestTenant(
            store.Id,
            store.Slug,
            $"customer-{uniqueId}@sessiontransport.invalid",
            $"customer-{uniqueId}");

        // Create the store before its customer because StoreId is an FK.
        await using (var storeScope =
                     _factory.Services.CreateAsyncScope())
        {
            var db = storeScope.ServiceProvider
                .GetRequiredService<IdentitySpikeDbContext>();

            db.Stores.Add(store);
            await db.SaveChangesAsync();
        }

        try
        {
            // Each customer is created under its own request-like DI scope.
            await using var identityScope =
                _factory.Services.CreateAsyncScope();

            var services = identityScope.ServiceProvider;

            var accessor = services
                .GetRequiredService<IIdentityLookupScopeAccessor>();

            accessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(store.Id));

            var userManager = services
                .GetRequiredService<UserManager<ApplicationUser>>();

            var user = new ApplicationUser
            {
                Id = tenant.UserId,
                UserName = tenant.UserName,
                Email = tenant.Email,
                EmailConfirmed = true,
                AccountKind = AccountKind.StoreCustomer,
                StoreId = store.Id,
                LockoutEnabled = true
            };

            var result = await userManager.CreateAsync(
                user,
                TestPassword);

            if (!result.Succeeded)
            {
                var errors = string.Join(
                    "; ",
                    result.Errors.Select(error =>
                        $"{error.Code}: {error.Description}"));

                throw new InvalidOperationException(
                    $"Could not create integration test customer: {errors}");
            }

            return tenant;
        }
        catch
        {
            await RemoveTestDataAsync([tenant]);
            throw;
        }
    }

    private async Task RemoveTestDataAsync(
        IReadOnlyCollection<TestTenant> tenants)
    {
        if (tenants.Count == 0)
        {
            return;
        }

        var userIds = tenants
            .Select(tenant => tenant.UserId)
            .ToArray();

        var storeIds = tenants
            .Select(tenant => tenant.StoreId)
            .ToArray();

        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        // Remove users before stores because StoreId has an FK.
        var users = await db.Users
            .Where(user => userIds.Contains(user.Id))
            .ToListAsync();

        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();

        var stores = await db.Stores
            .Where(store => storeIds.Contains(store.Id))
            .ToListAsync();

        db.Stores.RemoveRange(stores);
        await db.SaveChangesAsync();
    }

    private static IssuedCookie ReadIssuedCookie(
        HttpResponseMessage response,
        Guid storeId,
        string slug)
    {
        var headers = GetSetCookieHeaders(response);

        var expectedName = BuildCookieName(storeId);

        var mainCookie = Assert.Single(
            headers.Where(header =>
                header.StartsWith(
                    $"{expectedName}=",
                    StringComparison.Ordinal)));

        Assert.Contains(
            $"path=/api/stores/{slug}/customers",
            mainCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Secure",
            mainCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "HttpOnly",
            mainCookie,
            StringComparison.OrdinalIgnoreCase);

        // Preserve all cookie chunks if ChunkingCookieManager creates them.
        var cookieHeader = string.Join(
            "; ",
            headers.Select(header =>
                header.Split(';', 2)[0]));

        return new IssuedCookie(
            expectedName,
            cookieHeader);
    }

    private static IReadOnlyList<string> GetSetCookieHeaders(
        HttpResponseMessage response)
    {
        return response.Headers.TryGetValues(
            "Set-Cookie",
            out var values)
            ? values.ToArray()
            : Array.Empty<string>();
    }

    private static async Task<HttpResponseMessage> SendWithCookieAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        string cookieHeader)
    {
        using var request = new HttpRequestMessage(method, path);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookieHeader);

        return await client.SendAsync(request);
    }

    private static string BuildCookieName(Guid storeId)
    {
        return
            $"{StoreCustomerCookieManager.BaseCookieName}.store.{storeId:N}";
    }

    private static string RemapCookieName(
        string cookieHeader,
        string sourceName,
        string targetName)
    {
        var pairs = cookieHeader.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);

        var remappedPairs = pairs.Select(pair =>
        {
            var separator = pair.IndexOf('=');

            if (separator <= 0)
            {
                return pair;
            }

            var name = pair[..separator];

            var isMainCookie = string.Equals(
                name,
                sourceName,
                StringComparison.Ordinal);

            var isChunk = name.StartsWith(
                $"{sourceName}C",
                StringComparison.Ordinal);

            if (!isMainCookie && !isChunk)
            {
                return pair;
            }

            return
                $"{targetName}{name[sourceName.Length..]}" +
                pair[separator..];
        });

        return string.Join("; ", remappedPairs);
    }

    private sealed record TestTenant(
        Guid StoreId,
        string Slug,
        string Email,
        string UserId)
    {
        public string UserName => UserId;
    }

    private sealed record IssuedCookie(
        string Name,
        string CookieHeader);

    private sealed record MeResponse(
        string? UserId,
        string? AccountKind,
        string? StoreId);
}
