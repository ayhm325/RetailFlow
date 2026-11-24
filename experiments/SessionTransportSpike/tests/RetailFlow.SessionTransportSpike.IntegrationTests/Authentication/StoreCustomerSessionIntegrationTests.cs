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
using RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.Authentication;
using RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Authentication;

public sealed class StoreCustomerSessionIntegrationTests
    : IClassFixture<SessionTransportApiFactory>
{
    private const string TrustedBrowserOrigin =
        "https://localhost";

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

            // 1. Login through the real HTTP endpoint with valid same-origin CSRF headers.
            using var loginResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = TestPassword
                },
                origin: TrustedBrowserOrigin,
                customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                secFetchSite: "same-origin");

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

            // 3. Logout without custom CSRF header must be rejected with 403 Forbidden.
            using var missingHeaderLogoutResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/logout",
                body: null,
                cookieHeader: issuedCookie.CookieHeader,
                origin: TrustedBrowserOrigin,
                customHeaderValue: null,
                secFetchSite: "same-origin");

            Assert.Equal(
                HttpStatusCode.Forbidden,
                missingHeaderLogoutResponse.StatusCode);

            // 4. Logout from an untrusted Origin must be rejected with 403 Forbidden.
            using var untrustedOriginLogoutResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/logout",
                body: null,
                cookieHeader: issuedCookie.CookieHeader,
                origin: "https://evil.example",
                customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                secFetchSite: "same-origin");

            Assert.Equal(
                HttpStatusCode.Forbidden,
                untrustedOriginLogoutResponse.StatusCode);

            // 5. Logout with valid custom header, trusted origin, and same-origin Fetch Metadata.
            using var logoutResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/logout",
                body: null,
                cookieHeader: issuedCookie.CookieHeader,
                origin: TrustedBrowserOrigin,
                customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                secFetchSite: "same-origin");

            Assert.Equal(
                HttpStatusCode.NoContent,
                logoutResponse.StatusCode);

            // 6. The deletion cookie must use the same name and path.
            var deletionHeaders = GetSetCookieHeaders(logoutResponse);

            var deletionCookie = Assert.Single(
                deletionHeaders,
                header => header.StartsWith(
                    $"{issuedCookie.Name}=",
                    StringComparison.Ordinal));

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

            // 7. Replay the ORIGINAL authentication cookie after Logout.
            //
            // The browser deletion response is not sufficient evidence.
            // The server must reject the original session key because
            // its server-side ticket has already been removed.
            using var replayAfterLogoutResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Get,
                $"{basePath}/me",
                issuedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                replayAfterLogoutResponse.StatusCode);
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
            using var loginResponse = await SendBrowserPostAsync(
                client,
                $"{storeAPath}/login",
                new
                {
                    Email = storeA.Email,
                    Password = TestPassword
                },
                origin: TrustedBrowserOrigin,
                customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                secFetchSite: "same-origin");

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
                HttpStatusCode.Unauthorized,
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

            // Authentication events must reject the mismatched StoreId claim
            // with 401 Unauthorized.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                forgedResponse.StatusCode);

            var forgedResponseCookies =
                GetSetCookieHeaders(forgedResponse);

            // Rejection must delete the cookie selected for Store B,
            // using Store B's path.
            var rejectedCookie = Assert.Single(
                forgedResponseCookies,
                header => header.StartsWith(
                    $"{BuildCookieName(storeB.StoreId)}=",
                    StringComparison.Ordinal));

            Assert.Contains(
                $"path=/api/stores/{storeB.Slug}/customers",
                rejectedCookie,
                StringComparison.OrdinalIgnoreCase);

            // Case 3:
            // A rejected cross-store replay must not revoke Store A's
            // original server-side session.
            using var originalStoreAResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Get,
                $"{storeAPath}/me",
                storeACookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                originalStoreAResponse.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task CsrfProtection_EnforcesCustomHeader_OriginReferer_AndFetchMetadata()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateClient();

            var tenant = await CreateTenantAsync("csrf-policy");
            tenants.Add(tenant);

            var loginPath =
                $"/api/stores/{tenant.Slug}/customers/login";

            var credentials = new
            {
                Email = tenant.Email,
                Password = TestPassword
            };

            // 1. Missing custom header -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: TrustedBrowserOrigin,
                       customHeaderValue: null,
                       secFetchSite: "same-origin"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 2. External Origin with valid custom header -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: "https://evil.example",
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "same-origin"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 3. Origin: null -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: "null",
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "same-origin"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 4. Missing both Origin and Referer -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: null,
                       referer: null,
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "same-origin"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 5. Valid Origin + Sec-Fetch-Site: cross-site -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: TrustedBrowserOrigin,
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "cross-site"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 6. Valid Origin + Sec-Fetch-Site: same-site -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: TrustedBrowserOrigin,
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "same-site"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 6b. Valid Origin + Sec-Fetch-Site: none -> 403, no auth cookie.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: TrustedBrowserOrigin,
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "none"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 6c. Invalid Origin must NOT fall back to valid Referer -> 403.
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: "https://evil.example",
                       referer: $"{TrustedBrowserOrigin}/store/login",
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "same-origin"))
            {
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.Empty(GetSetCookieHeaders(response));
            }

            // 7. Valid Origin + valid Header + absent Fetch Metadata -> allowed (204).
            using (var response = await SendBrowserPostAsync(
                       client,
                       loginPath,
                       credentials,
                       origin: TrustedBrowserOrigin,
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: null))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                ReadIssuedCookie(response, tenant.StoreId, tenant.Slug);
            }

            // 8. Absent Origin + valid Referer + valid Header -> allowed (204).
            // Use a fresh client without the session cookie issued in step 7
            // so the active-session 409 guard does not trigger.
            using var refererClient = CreateClient();
            using (var response = await SendBrowserPostAsync(
                       refererClient,
                       loginPath,
                       credentials,
                       origin: null,
                       referer: $"{TrustedBrowserOrigin}/{tenant.Slug}/login",
                       customHeaderValue: BrowserCsrfProtectionMiddleware.CustomHeaderValue,
                       secFetchSite: "same-origin"))
            {
                Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
                ReadIssuedCookie(response, tenant.StoreId, tenant.Slug);
            }
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

        var ticketFactory = scope.ServiceProvider
            .GetRequiredService<
                IDbContextFactory<SessionTicketDbContext>>();

        await using var ticketDb =
            await ticketFactory.CreateDbContextAsync();

        await ticketDb.AuthenticationTickets
            .Where(ticket =>
                userIds.Contains(ticket.UserId) ||
                storeIds.Contains(ticket.StoreId))
            .ExecuteDeleteAsync();

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
            headers,
            header => header.StartsWith(
                $"{expectedName}=",
                StringComparison.Ordinal));

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

    private static async Task<HttpResponseMessage> SendBrowserPostAsync(
        HttpClient client,
        string path,
        object? body,
        string? origin,
        string? customHeaderValue,
        string? secFetchSite,
        string? cookieHeader = null,
        string? referer = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                cookieHeader);
        }

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "Origin",
                origin);
        }

        if (referer is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "Referer",
                referer);
        }

        if (customHeaderValue is not null)
        {
            request.Headers.TryAddWithoutValidation(
                BrowserCsrfProtectionMiddleware.CustomHeaderName,
                customHeaderValue);
        }

        if (secFetchSite is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "Sec-Fetch-Site",
                secFetchSite);
        }

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
