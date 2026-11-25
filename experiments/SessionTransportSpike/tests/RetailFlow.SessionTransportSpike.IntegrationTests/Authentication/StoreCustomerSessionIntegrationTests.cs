using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.Api.Authentication;
using RetailFlow.SessionTransportSpike.Api.Infrastructure.DataProtection;
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

    private const string ChangedPassword =
        "RetailFlow-Changed-2026!Strong";

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
    public async Task StoreACookie_CannotAccessStoreB()
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

            // The ticket belongs to a valid Store A session,
            // but the authorization policy denies access to Store B.
            Assert.Equal(
                HttpStatusCode.Forbidden,
                forgedResponse.StatusCode);

            Assert.Null(forgedResponse.Headers.Location);

            Assert.Empty(
                GetSetCookieHeaders(forgedResponse));

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
    public async Task StoreACookie_ReplayedAgainstStoreBAfterSecurityStampInterval_ReturnsForbidden_AndPreservesStoreASession()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var fakeTimeProvider = new FakeTimeProvider(
                DateTimeOffset.UtcNow);

            using var hostFactory = SessionTransportApiFactory.Create(
                RetailFlowDataProtectionDefaults.ApplicationName,
                TestDataProtectionCertificates.Shared.Path,
                TestDataProtectionCertificates.Shared.Password,
                fakeTimeProvider);

            using var client = CreateStatelessClient(hostFactory);

            var storeA = await CreateTenantAsync("secstamp-store-a");
            tenants.Add(storeA);

            var storeB = await CreateTenantAsync("secstamp-store-b");
            tenants.Add(storeB);

            var storeAPath =
                $"/api/stores/{storeA.Slug}/customers";

            var storeBPath =
                $"/api/stores/{storeB.Slug}/customers";

            // 1. Login to Store A and capture Cookie A.
            var storeACookie = await LoginCustomerAsync(
                client,
                storeA);

            // 2. Rename Cookie A to Store B's cookie name.
            var forgedCookieHeader = RemapCookieName(
                storeACookie.CookieHeader,
                storeACookie.Name,
                BuildCookieName(storeB.StoreId));

            // 3. Advance beyond SecurityStampValidatorOptions.ValidationInterval (5m),
            //    while remaining well within the 15-minute idle timeout.
            fakeTimeProvider.Advance(TimeSpan.FromMinutes(6));

            // 4. Replay the remapped cookie against Store B -> must return 403,
            //    with no Set-Cookie and without revoking Store A's session.
            using var forgedResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Get,
                $"{storeBPath}/me",
                forgedCookieHeader);

            Assert.Equal(
                HttpStatusCode.Forbidden,
                forgedResponse.StatusCode);

            Assert.Null(forgedResponse.Headers.Location);

            Assert.Empty(
                GetSetCookieHeaders(forgedResponse));

            // 5. Verify Store A's server-side session still exists in SQL.
            await using (var scope =
                         hostFactory.Services.CreateAsyncScope())
            {
                var factory = scope.ServiceProvider
                    .GetRequiredService<
                        IDbContextFactory<SessionTicketDbContext>>();

                await using var db =
                    await factory.CreateDbContextAsync();

                var storeASessions = await db.AuthenticationTickets
                    .AsNoTracking()
                    .Where(ticket =>
                        ticket.UserId == storeA.UserId &&
                        ticket.StoreId == storeA.StoreId)
                    .ToListAsync();

                Assert.Single(storeASessions);
            }

            // 6. Replay original Cookie A against Store A -> SecurityStampValidator
            //    validates normally in Store A's scope and returns 200 OK.
            using var storeAResponse = await SendWithCookieAsync(
                client,
                HttpMethod.Get,
                $"{storeAPath}/me",
                storeACookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                storeAResponse.StatusCode);
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

    [Fact]
    public async Task SlidingRenewal_UpdatesExistingSessionWithoutCreatingAnother()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            var tenant = await CreateTenantAsync("sliding-renewal");
            tenants.Add(tenant);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            // 1. Login creates exactly one server-side session.
            var issuedCookie = await LoginCustomerAsync(
                client,
                tenant);

            var beforeRenewal = await ReadSingleSessionAsync(tenant);
            var initialExpiration = beforeRenewal.ExpiresUtc;
            var initialLastRenewed = beforeRenewal.LastRenewedUtc;

            Assert.Equal(
                1,
                await CountSessionsAsync(tenant.StoreId));

            // 2. Advance beyond half of the 15-minute lifetime.
            _factory.FakeTimeProvider.Advance(
                TimeSpan.FromMinutes(8));

            // 3. An authenticated request should trigger sliding renewal.
            using var meResponse = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                issuedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                meResponse.StatusCode);

            // 4. Cookie Handler should issue an updated cookie.
            var renewedCookie = ReadIssuedCookie(
                meResponse,
                tenant.StoreId,
                tenant.Slug);

            Assert.False(
                string.IsNullOrWhiteSpace(renewedCookie.CookieHeader));

            // 5. The SQL record must be updated in place.
            var afterRenewal = await ReadSingleSessionAsync(tenant);

            Assert.Equal(
                beforeRenewal.Key,
                afterRenewal.Key);

            Assert.True(
                afterRenewal.ExpiresUtc > initialExpiration,
                "Sliding renewal must extend ExpiresUtc.");

            Assert.True(
                afterRenewal.LastRenewedUtc > initialLastRenewed,
                "Sliding renewal must update LastRenewedUtc.");

            Assert.Equal(
                1,
                await CountSessionsAsync(tenant.StoreId));

            // 6. The renewed browser cookie must still authenticate.
            using var secondMeResponse = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                renewedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                secondMeResponse.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task Session_ExpiresAfterFifteenMinutesOfInactivity_ReturnsUnauthorizedAndRemovesTicket()
    {
        var tenants = new List<TestTenant>();

        try
        {
            // Create an independent API host and independent clock.
            // This prevents modifying the shared xUnit fixture clock.
            var fakeTimeProvider = new FakeTimeProvider(
                DateTimeOffset.UtcNow);

            using var expirationFactory =
                SessionTransportApiFactory.Create(
                    RetailFlowDataProtectionDefaults.ApplicationName,
                    TestDataProtectionCertificates.Shared.Path,
                    TestDataProtectionCertificates.Shared.Password,
                    fakeTimeProvider);

            using var client = CreateStatelessClient(
                expirationFactory);

            // Create test tenant and customer.
            var tenant = await CreateTenantAsync(
                "session-idle-expiration");

            tenants.Add(tenant);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            // 1. Login and capture the original browser cookie.
            var issuedCookie = await LoginCustomerAsync(
                client,
                tenant);

            // 2. Read the actual persisted SQL session.
            PersistedAuthenticationTicket session;

            await using (var scope =
                         expirationFactory.Services.CreateAsyncScope())
            {
                var factory = scope.ServiceProvider
                    .GetRequiredService<
                        IDbContextFactory<SessionTicketDbContext>>();

                await using var db =
                    await factory.CreateDbContextAsync();

                var sessions = await db.AuthenticationTickets
                    .AsNoTracking()
                    .Where(ticket =>
                        ticket.UserId == tenant.UserId &&
                        ticket.StoreId == tenant.StoreId)
                    .ToListAsync();

                session = Assert.Single(sessions);
            }

            // 3. Verify the configured 15-minute idle lifetime and 8-hour absolute lifetime.
            Assert.Equal(
                TimeSpan.FromMinutes(15),
                session.ExpiresUtc - session.CreatedUtc);

            Assert.Equal(
                TimeSpan.FromHours(8),
                session.AbsoluteExpiresUtc - session.CreatedUtc);

            Assert.True(
                session.ExpiresUtc > fakeTimeProvider.GetUtcNow());

            // 4. Stop the periodic cleanup worker for this dedicated
            // test host. We want RetrieveAsync itself to handle expiry.
            var cleanupService = expirationFactory.Services
                .GetRequiredService<
                    ExpiredSessionTicketCleanupService>();

            await cleanupService.StopAsync(
                CancellationToken.None);

            // 5. Advance time beyond this exact ticket expiration.
            var remainingLifetime =
                session.ExpiresUtc - fakeTimeProvider.GetUtcNow();

            Assert.True(remainingLifetime > TimeSpan.Zero);

            fakeTimeProvider.Advance(
                remainingLifetime + TimeSpan.FromSeconds(1));

            // 6. Replay the ORIGINAL browser cookie.
            using var expiredResponse = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                issuedCookie.CookieHeader);

            // The expired ticket must no longer authenticate.
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                expiredResponse.StatusCode);

            // 7. RetrieveAsync must remove the expired SQL session.
            await using (var verificationScope =
                         expirationFactory.Services.CreateAsyncScope())
            {
                var factory = verificationScope.ServiceProvider
                    .GetRequiredService<
                        IDbContextFactory<SessionTicketDbContext>>();

                await using var db =
                    await factory.CreateDbContextAsync();

                var remainingSessions =
                    await db.AuthenticationTickets
                        .CountAsync(ticket =>
                            ticket.StoreId == tenant.StoreId);

                Assert.Equal(0, remainingSessions);
            }
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task Session_ExpiresAtAbsoluteLifetime_EvenWhileContinuouslyActive()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var fakeTimeProvider = new FakeTimeProvider(
                DateTimeOffset.UtcNow);

            // The test uses 20 minutes instead of the production 8 hours.
            // Idle timeout remains 15 minutes.
            using var expirationFactory =
                SessionTransportApiFactory.Create(
                    RetailFlowDataProtectionDefaults.ApplicationName,
                    TestDataProtectionCertificates.Shared.Path,
                    TestDataProtectionCertificates.Shared.Password,
                    fakeTimeProvider,
                    absoluteLifetime: TimeSpan.FromMinutes(20));

            using var client = CreateStatelessClient(
                expirationFactory);

            var tenant = await CreateTenantAsync(
                "absolute-session-expiration");

            tenants.Add(tenant);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            // 1. Login and capture the original cookie.
            var issuedCookie = await LoginCustomerAsync(
                client,
                tenant);

            // 2. Read the actual session from SQL Server.
            async Task<PersistedAuthenticationTicket> ReadSessionAsync()
            {
                await using var scope =
                    expirationFactory.Services.CreateAsyncScope();

                var factory = scope.ServiceProvider
                    .GetRequiredService<
                        IDbContextFactory<SessionTicketDbContext>>();

                await using var db =
                    await factory.CreateDbContextAsync();

                var sessions = await db.AuthenticationTickets
                    .AsNoTracking()
                    .Where(ticket =>
                        ticket.UserId == tenant.UserId &&
                        ticket.StoreId == tenant.StoreId)
                    .ToListAsync();

                return Assert.Single(sessions);
            }

            async Task<int> CountSessionsAsync()
            {
                await using var scope =
                    expirationFactory.Services.CreateAsyncScope();

                var factory = scope.ServiceProvider
                    .GetRequiredService<
                        IDbContextFactory<SessionTicketDbContext>>();

                await using var db =
                    await factory.CreateDbContextAsync();

                return await db.AuthenticationTickets
                    .CountAsync(ticket =>
                        ticket.StoreId == tenant.StoreId);
            }

            var initialSession = await ReadSessionAsync();

            // 3. Verify the two independent deadlines.
            Assert.Equal(
                TimeSpan.FromMinutes(15),
                initialSession.ExpiresUtc - initialSession.CreatedUtc);

            Assert.Equal(
                TimeSpan.FromMinutes(20),
                initialSession.AbsoluteExpiresUtc - initialSession.CreatedUtc);

            Assert.Equal(
                1,
                await CountSessionsAsync());

            // 4. Stop the periodic cleanup worker.
            // The test must prove that RetrieveAsync enforces expiration.
            var cleanupService = expirationFactory.Services
                .GetRequiredService<ExpiredSessionTicketCleanupService>();

            await cleanupService.StopAsync(
                CancellationToken.None);

            // 5. Advance 8 minutes.
            // This is beyond half the 15-minute idle timeout.
            fakeTimeProvider.Advance(
                TimeSpan.FromMinutes(8));

            using var firstActivity = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                issuedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                firstActivity.StatusCode);

            var firstRenewedCookie = ReadIssuedCookie(
                firstActivity,
                tenant.StoreId,
                tenant.Slug);

            var afterFirstRenewal = await ReadSessionAsync();

            // Sliding renewal may extend ExpiresUtc,
            // but it must not change the absolute deadline.
            Assert.Equal(
                initialSession.Key,
                afterFirstRenewal.Key);

            Assert.Equal(
                initialSession.AbsoluteExpiresUtc,
                afterFirstRenewal.AbsoluteExpiresUtc);

            // The requested sliding expiration would exceed 20 minutes.
            // It must be clamped to the absolute deadline.
            Assert.Equal(
                afterFirstRenewal.AbsoluteExpiresUtc,
                afterFirstRenewal.ExpiresUtc);

            // 6. Keep the user active for another 8 minutes.
            fakeTimeProvider.Advance(
                TimeSpan.FromMinutes(8));

            using var secondActivity = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                firstRenewedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                secondActivity.StatusCode);

            var secondRenewedCookie = ReadIssuedCookie(
                secondActivity,
                tenant.StoreId,
                tenant.Slug);

            var afterSecondRenewal = await ReadSessionAsync();

            Assert.Equal(
                initialSession.AbsoluteExpiresUtc,
                afterSecondRenewal.AbsoluteExpiresUtc);

            Assert.Equal(
                initialSession.AbsoluteExpiresUtc,
                afterSecondRenewal.ExpiresUtc);

            // 7. Advance beyond the absolute deadline.
            // The user was active at minute 16, but activity must not
            // extend the session beyond minute 20.
            fakeTimeProvider.Advance(
                TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(1));

            using var expiredResponse = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                secondRenewedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                expiredResponse.StatusCode);

            // 8. RetrieveAsync must remove the expired SQL session.
            Assert.Equal(
                0,
                await CountSessionsAsync());
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task RenewAsync_WhenStaleRenewalArrivesLater_DoesNotOverwriteNewerSession()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            var tenant = await CreateTenantAsync("stale-renewal");
            tenants.Add(tenant);

            _ = await LoginCustomerAsync(
                client,
                tenant);

            var session = await ReadSingleSessionAsync(tenant);

            var ticketStore = _factory.Services
                .GetRequiredService<ITicketStore>();

            var originalTicket =
                await ticketStore.RetrieveAsync(session.Key);

            Assert.NotNull(originalTicket);

            var rawNow = _factory.FakeTimeProvider.GetUtcNow();
            // AuthenticationProperties serializes ExpiresUtc with 1-second resolution (RFC1123).
            var now = new DateTimeOffset(
                rawNow.Year,
                rawNow.Month,
                rawNow.Day,
                rawNow.Hour,
                rawNow.Minute,
                rawNow.Second,
                TimeSpan.Zero);

            // 1. Simulate the newer renewal.
            var newerExpiration = now.AddMinutes(30);
            var newerTicket = CreateTicketWithTimes(
                originalTicket,
                now,
                newerExpiration);

            await ticketStore.RenewAsync(
                session.Key,
                newerTicket);

            var afterNewerRenewal = await ReadSingleSessionAsync(tenant);

            Assert.Equal(
                newerExpiration,
                afterNewerRenewal.ExpiresUtc);

            var protectedTicketAfterNewerRenewal =
                afterNewerRenewal.ProtectedTicket.ToArray();

            // 2. Simulate an older request arriving later.
            var staleExpiration = now.AddMinutes(20);
            var staleTicket = CreateTicketWithTimes(
                originalTicket,
                now.AddMinutes(-1),
                staleExpiration);

            await ticketStore.RenewAsync(
                session.Key,
                staleTicket);

            // 3. The older renewal must not overwrite the newer one.
            var finalSession = await ReadSingleSessionAsync(tenant);

            Assert.Equal(
                newerExpiration,
                finalSession.ExpiresUtc);

            Assert.Equal(
                protectedTicketAfterNewerRenewal,
                finalSession.ProtectedTicket);

            Assert.Equal(
                afterNewerRenewal.LastRenewedUtc,
                finalSession.LastRenewedUtc);

            // 4. Retrieved ticket must still contain the newer expiration.
            var retrievedTicket =
                await ticketStore.RetrieveAsync(session.Key);

            Assert.NotNull(retrievedTicket);

            Assert.Equal(
                newerExpiration,
                retrievedTicket.Properties.ExpiresUtc);

            Assert.Equal(
                1,
                await CountSessionsAsync(tenant.StoreId));
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task RenewAndRemove_WhenExecutedConcurrently_KeepSessionRevoked()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            var tenant = await CreateTenantAsync("renew-remove-race");
            tenants.Add(tenant);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            var issuedCookie = await LoginCustomerAsync(
                client,
                tenant);

            var session = await ReadSingleSessionAsync(tenant);

            var ticketStore = _factory.Services
                .GetRequiredService<ITicketStore>();

            var originalTicket =
                await ticketStore.RetrieveAsync(session.Key);

            Assert.NotNull(originalTicket);

            var rawNow = _factory.FakeTimeProvider.GetUtcNow();
            // AuthenticationProperties serializes ExpiresUtc with 1-second resolution (RFC1123).
            var now = new DateTimeOffset(
                rawNow.Year,
                rawNow.Month,
                rawNow.Day,
                rawNow.Hour,
                rawNow.Minute,
                rawNow.Second,
                TimeSpan.Zero);

            var renewedTicket = CreateTicketWithTimes(
                originalTicket,
                now,
                now.AddMinutes(30));

            // Both operations use independent DbContext instances.
            var renewTask = ticketStore.RenewAsync(
                session.Key,
                renewedTicket);

            var removeTask = ticketStore.RemoveAsync(
                session.Key);

            await Task.WhenAll(renewTask, removeTask);

            // The session must remain revoked regardless of SQL execution order.
            var retrievedTicket =
                await ticketStore.RetrieveAsync(session.Key);

            Assert.Null(retrievedTicket);

            Assert.Equal(
                0,
                await CountSessionsAsync(tenant.StoreId));

            // Replay the original browser cookie.
            using var replayResponse = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                issuedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                replayResponse.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task Login_WhenAnotherSessionIsAlreadyAuthenticated_ReturnsConflict()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            // One store, two different StoreCustomer users.
            var firstCustomer = await CreateTenantAsync("account-switch");
            tenants.Add(firstCustomer);

            var secondCustomer =
                await CreateCustomerForExistingStoreAsync(
                    firstCustomer,
                    "account-switch-second");
            tenants.Add(secondCustomer);

            var basePath =
                $"/api/stores/{firstCustomer.Slug}/customers";

            // 1. First customer logs in.
            var originalCookie = await LoginCustomerAsync(
                client,
                firstCustomer);

            var originalSession =
                await ReadSingleSessionAsync(firstCustomer);

            Assert.Equal(
                firstCustomer.UserId,
                originalSession.UserId);

            Assert.Equal(
                1,
                await CountSessionsAsync(firstCustomer.StoreId));

            // 2. Attempt to log in as the second customer
            // while sending the first customer's active cookie.
            using var secondLoginResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/login",
                new
                {
                    Email = secondCustomer.Email,
                    Password = TestPassword
                },
                originalCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Conflict,
                secondLoginResponse.StatusCode);

            // 3. The rejected login must not issue another cookie.
            Assert.Empty(
                GetSetCookieHeaders(secondLoginResponse));

            // 4. The original server-side session must remain unchanged.
            var remainingSession =
                await ReadSingleSessionAsync(firstCustomer);

            Assert.Equal(
                originalSession.Key,
                remainingSession.Key);

            Assert.Equal(
                firstCustomer.UserId,
                remainingSession.UserId);

            Assert.Equal(
                1,
                await CountSessionsAsync(firstCustomer.StoreId));

            // 5. The first customer's original cookie must still work.
            using var meResponse = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                originalCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                meResponse.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task CleanupExpiredTickets_RemovesOnlyExpiredSessions()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            // Create two independent stores and customers.
            var expiredTenant =
                await CreateTenantAsync("expired-session");

            tenants.Add(expiredTenant);

            var activeTenant =
                await CreateTenantAsync("active-session");

            tenants.Add(activeTenant);

            var expiredPath =
                $"/api/stores/{expiredTenant.Slug}/customers";

            var activePath =
                $"/api/stores/{activeTenant.Slug}/customers";

            // Login both customers.
            var expiredCookie =
                await LoginCustomerAsync(client, expiredTenant);

            var activeCookie =
                await LoginCustomerAsync(client, activeTenant);

            var expiredSession =
                await ReadSingleSessionAsync(expiredTenant);

            var activeSession =
                await ReadSingleSessionAsync(activeTenant);

            Assert.Equal(
                1,
                await CountSessionsAsync(expiredTenant.StoreId));

            Assert.Equal(
                1,
                await CountSessionsAsync(activeTenant.StoreId));

            // Move only the first session's database expiration into the past.
            var expiredAt =
                _factory.FakeTimeProvider.GetUtcNow()
                    .AddMinutes(-1);

            await using (var scope =
                         _factory.Services.CreateAsyncScope())
            {
                var factory = scope.ServiceProvider
                    .GetRequiredService<
                        IDbContextFactory<SessionTicketDbContext>>();

                await using var db =
                    await factory.CreateDbContextAsync();

                var affectedRows =
                    await db.AuthenticationTickets
                        .Where(ticket => ticket.Key == expiredSession.Key)
                        .ExecuteUpdateAsync(
                            setters => setters.SetProperty(
                                ticket => ticket.ExpiresUtc,
                                expiredAt));

                Assert.Equal(1, affectedRows);
            }

            // Verify the second session remains active before cleanup.
            Assert.Equal(
                1,
                await CountSessionsAsync(activeTenant.StoreId));

            // Execute exactly one cleanup cycle.
            var cleanupService = _factory.Services
                .GetRequiredService<ExpiredSessionTicketCleanupService>();

            var deletedCount =
                await cleanupService.CleanupExpiredTicketsAsync();

            Assert.Equal(1, deletedCount);

            // The expired session must be gone.
            Assert.Equal(
                0,
                await CountSessionsAsync(expiredTenant.StoreId));

            // The active session must remain untouched.
            Assert.Equal(
                1,
                await CountSessionsAsync(activeTenant.StoreId));

            // Replaying the expired browser cookie must fail.
            using var expiredReplay = await SendAuthenticatedGetAsync(
                client,
                $"{expiredPath}/me",
                expiredCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                expiredReplay.StatusCode);

            // The active browser cookie must still work.
            using var activeRequest = await SendAuthenticatedGetAsync(
                client,
                $"{activePath}/me",
                activeCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.OK,
                activeRequest.StatusCode);

            // Verify that the active session key did not change.
            var remainingActiveSession =
                await ReadSingleSessionAsync(activeTenant);

            Assert.Equal(
                activeSession.Key,
                remainingActiveSession.Key);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task CleanupExpiredTickets_WhenAllSessionsAreActive_DeletesNothing()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            var tenant = await CreateTenantAsync("no-expired-sessions");
            tenants.Add(tenant);

            await LoginCustomerAsync(client, tenant);

            var cleanupService = _factory.Services
                .GetRequiredService<ExpiredSessionTicketCleanupService>();

            var deletedCount =
                await cleanupService.CleanupExpiredTicketsAsync();

            Assert.Equal(0, deletedCount);

            Assert.Equal(
                1,
                await CountSessionsAsync(tenant.StoreId));
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task InstanceA_CookieAndTicket_AreReadableByInstanceB_AndAfterRestart()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var clientA = CreateStatelessClient();

            var tenant = await CreateTenantAsync(
                "dataprotection-multi-instance");

            tenants.Add(tenant);

            // Instance A: Login.
            var originalCookie = await LoginCustomerAsync(
                clientA,
                tenant);

            var originalSession = await ReadSingleSessionAsync(tenant);

            Assert.NotEmpty(originalSession.ProtectedTicket);

            var initialKeyCount = await CountDataProtectionKeysAsync();
            Assert.True(initialKeyCount > 0);

            // Instance B: Independent host and independent DataProtectionProvider.
            using (var instanceB = CreateDataProtectionInstance())
            {
                using var clientB = CreateStatelessClient(instanceB);

                // Directly retrieve and unprotect the SQL AuthenticationTicket.
                var ticketStoreB = instanceB.Services
                    .GetRequiredService<ITicketStore>();

                var recoveredTicket = await ticketStoreB.RetrieveAsync(
                    originalSession.Key);

                Assert.NotNull(recoveredTicket);

                Assert.Equal(
                    tenant.UserId,
                    recoveredTicket.Principal.FindFirst(
                        System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                // Full HTTP flow also verifies Cookie unprotection.
                using var responseB = await SendAuthenticatedGetAsync(
                    clientB,
                    $"/api/stores/{tenant.Slug}/customers/me",
                    originalCookie.CookieHeader);

                Assert.Equal(
                    HttpStatusCode.OK,
                    responseB.StatusCode);
            }

            // Instance C: Fresh host after B has been shut down.
            using (var instanceC = CreateDataProtectionInstance())
            {
                using var clientC = CreateStatelessClient(instanceC);

                var ticketStoreC = instanceC.Services
                    .GetRequiredService<ITicketStore>();

                var recoveredTicket = await ticketStoreC.RetrieveAsync(
                    originalSession.Key);

                Assert.NotNull(recoveredTicket);

                Assert.Equal(
                    tenant.UserId,
                    recoveredTicket.Principal.FindFirst(
                        System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                using var responseC = await SendAuthenticatedGetAsync(
                    clientC,
                    $"/api/stores/{tenant.Slug}/customers/me",
                    originalCookie.CookieHeader);

                Assert.Equal(
                    HttpStatusCode.OK,
                    responseC.StatusCode);
            }

            // No new session or key ring should be generated just because
            // another API instance was started.
            Assert.Equal(1, await CountSessionsAsync(tenant.StoreId));

            Assert.Equal(
                initialKeyCount,
                await CountDataProtectionKeysAsync());
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task DifferentApplicationName_CannotUnprotectCookieOrTicket()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var clientA = CreateStatelessClient();

            var tenant = await CreateTenantAsync(
                "dataprotection-wrong-app");

            tenants.Add(tenant);

            var originalCookie = await LoginCustomerAsync(
                clientA,
                tenant);

            var session = await ReadSingleSessionAsync(tenant);

            using var wrongInstance = CreateDataProtectionInstance(
                applicationName: "RetailFlow.Auth.Wrong");

            using var wrongClient = CreateStatelessClient(wrongInstance);

            var ticketStore = wrongInstance.Services
                .GetRequiredService<ITicketStore>();

            var ticket = await ticketStore.RetrieveAsync(session.Key);

            Assert.Null(ticket);

            using var response = await SendAuthenticatedGetAsync(
                wrongClient,
                $"/api/stores/{tenant.Slug}/customers/me",
                originalCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task WrongCertificate_CannotUnprotectCookieOrTicket()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var clientA = CreateStatelessClient();

            var tenant = await CreateTenantAsync(
                "dataprotection-wrong-cert");

            tenants.Add(tenant);

            var originalCookie = await LoginCustomerAsync(
                clientA,
                tenant);

            var session = await ReadSingleSessionAsync(tenant);

            using var wrongInstance = CreateDataProtectionInstance(
                certificate: TestDataProtectionCertificates.Wrong);

            using var wrongClient = CreateStatelessClient(wrongInstance);

            var ticketStore = wrongInstance.Services
                .GetRequiredService<ITicketStore>();

            var ticket = await ticketStore.RetrieveAsync(session.Key);

            Assert.Null(ticket);

            using var response = await SendAuthenticatedGetAsync(
                wrongClient,
                $"/api/stores/{tenant.Slug}/customers/me",
                originalCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    private SessionTransportApiFactory CreateDataProtectionInstance(
        string? applicationName = null,
        TestDataProtectionCertificate? certificate = null)
    {
        var selectedCertificate =
            certificate ?? TestDataProtectionCertificates.Shared;

        return SessionTransportApiFactory.Create(
            applicationName ??
                RetailFlowDataProtectionDefaults.ApplicationName,
            selectedCertificate.Path,
            selectedCertificate.Password,
            _factory.FakeTimeProvider);
    }

    private static HttpClient CreateStatelessClient(
        SessionTransportApiFactory factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });
    }

    private async Task<int> CountDataProtectionKeysAsync()
    {
        await using var scope =
            _factory.Services.CreateAsyncScope();

        var factory = scope.ServiceProvider
            .GetRequiredService<
                IDbContextFactory<SessionTicketDbContext>>();

        await using var db = await factory.CreateDbContextAsync();

        var keys = await db.DataProtectionKeys
            .AsNoTracking()
            .ToListAsync();

        Assert.NotEmpty(keys);

        foreach (var key in keys)
        {
            Assert.False(string.IsNullOrWhiteSpace(key.Xml));

            var document = System.Xml.Linq.XDocument.Parse(key.Xml!);

            Assert.True(
                document.Descendants().Any(
                    element => element.Name.LocalName == "encryptedSecret"),
                "A Data Protection key was stored without the expected " +
                "certificate-encrypted secret.");
        }

        return keys.Count;
    }

    [Fact]
    public async Task ChangePassword_RevokesAllUserSessions_AndRequiresNewPassword()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var clientA = CreateStatelessClient();
            using var clientB = CreateStatelessClient();
            using var otherStoreClient = CreateStatelessClient();
            using var oldPasswordClient = CreateStatelessClient();
            using var newPasswordClient = CreateStatelessClient();

            var tenant = await CreateTenantAsync("password-change");
            tenants.Add(tenant);

            var otherTenant =
                await CreateTenantAsync("password-change-other-store");
            tenants.Add(otherTenant);

            var cookieA = await LoginCustomerAsync(clientA, tenant);
            var cookieB = await LoginCustomerAsync(clientB, tenant);

            var otherCookie =
                await LoginCustomerAsync(otherStoreClient, otherTenant);

            Assert.Equal(2, await CountSessionsAsync(tenant.StoreId));
            Assert.Equal(1, await CountSessionsAsync(otherTenant.StoreId));

            var before = await ReadUserCredentialStateAsync(tenant.UserId);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            using var changeResponse = await SendBrowserPostAsync(
                clientA,
                $"{basePath}/password/change",
                new
                {
                    CurrentPassword = TestPassword,
                    NewPassword = ChangedPassword
                },
                cookieA.CookieHeader);

            Assert.Equal(
                HttpStatusCode.NoContent,
                changeResponse.StatusCode);

            // The current browser must receive cookie deletion.
            var deletionHeaders =
                GetSetCookieHeaders(changeResponse);

            var deletionCookie = Assert.Single(
                deletionHeaders,
                header => header.StartsWith(
                    $"{cookieA.Name}=",
                    StringComparison.Ordinal));

            Assert.True(
                deletionCookie.Contains(
                    "expires=",
                    StringComparison.OrdinalIgnoreCase) ||
                deletionCookie.Contains(
                    "max-age=0",
                    StringComparison.OrdinalIgnoreCase));

            var after = await ReadUserCredentialStateAsync(tenant.UserId);

            Assert.NotEqual(before.PasswordHash, after.PasswordHash);
            Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);

            // Every session for StoreCustomer A must be deleted.
            Assert.Equal(0, await CountSessionsAsync(tenant.StoreId));

            // Store B sessions must remain untouched.
            Assert.Equal(1, await CountSessionsAsync(otherTenant.StoreId));

            // Replay both old browser cookies.
            using var replayA = await SendAuthenticatedGetAsync(
                clientA,
                $"{basePath}/me",
                cookieA.CookieHeader);

            using var replayB = await SendAuthenticatedGetAsync(
                clientB,
                $"{basePath}/me",
                cookieB.CookieHeader);

            Assert.Equal(HttpStatusCode.Unauthorized, replayA.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, replayB.StatusCode);

            // Store B remains authenticated.
            using var otherMe = await SendAuthenticatedGetAsync(
                otherStoreClient,
                $"/api/stores/{otherTenant.Slug}/customers/me",
                otherCookie.CookieHeader);

            Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);

            // Old password must no longer work.
            using var oldLogin = await SendBrowserPostAsync(
                oldPasswordClient,
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = TestPassword
                });

            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);

            // New password must work.
            using var newLogin = await SendBrowserPostAsync(
                newPasswordClient,
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = ChangedPassword
                });

            Assert.Equal(HttpStatusCode.NoContent, newLogin.StatusCode);

            var newCookie = ReadIssuedCookie(
                newLogin,
                tenant.StoreId,
                tenant.Slug);

            using var newMe = await SendAuthenticatedGetAsync(
                newPasswordClient,
                $"{basePath}/me",
                newCookie.CookieHeader);

            Assert.Equal(HttpStatusCode.OK, newMe.StatusCode);

            Assert.Equal(1, await CountSessionsAsync(tenant.StoreId));
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task ChangePassword_FailedValidation_DoesNotRevokeSessions()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            var tenant = await CreateTenantAsync("password-change-failure");
            tenants.Add(tenant);

            var cookie = await LoginCustomerAsync(client, tenant);

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            var before =
                await ReadUserCredentialStateAsync(tenant.UserId);

            // 1. Wrong current password.
            using var wrongCurrentResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/password/change",
                new
                {
                    CurrentPassword = "Wrong-Current-Password!2026",
                    NewPassword = ChangedPassword
                },
                cookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                wrongCurrentResponse.StatusCode);

            Assert.Empty(GetSetCookieHeaders(wrongCurrentResponse));

            Assert.Equal(1, await CountSessionsAsync(tenant.StoreId));

            // 2. Weak new password.
            using var weakPasswordResponse = await SendBrowserPostAsync(
                client,
                $"{basePath}/password/change",
                new
                {
                    CurrentPassword = TestPassword,
                    NewPassword = "short"
                },
                cookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.BadRequest,
                weakPasswordResponse.StatusCode);

            Assert.Empty(GetSetCookieHeaders(weakPasswordResponse));

            // The password and stamp must remain unchanged.
            var after =
                await ReadUserCredentialStateAsync(tenant.UserId);

            Assert.Equal(before.PasswordHash, after.PasswordHash);
            Assert.Equal(before.SecurityStamp, after.SecurityStamp);

            // Existing session remains active.
            Assert.Equal(1, await CountSessionsAsync(tenant.StoreId));

            using var me = await SendAuthenticatedGetAsync(
                client,
                $"{basePath}/me",
                cookie.CookieHeader);

            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task SecurityStampChange_InvalidatesSessionsAtNextValidationInterval()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var fakeTimeProvider = new FakeTimeProvider(
                DateTimeOffset.UtcNow);

            using var stampFactory = SessionTransportApiFactory.Create(
                RetailFlowDataProtectionDefaults.ApplicationName,
                TestDataProtectionCertificates.Shared.Path,
                TestDataProtectionCertificates.Shared.Password,
                fakeTimeProvider);

            using var clientA = CreateStatelessClient(stampFactory);
            using var clientB = CreateStatelessClient(stampFactory);

            var tenant = await CreateTenantAsync("security-stamp-revocation");
            tenants.Add(tenant);

            var cookieA = await LoginCustomerAsync(clientA, tenant);
            var cookieB = await LoginCustomerAsync(clientB, tenant);

            Assert.Equal(2, await CountSessionsAsync(tenant.StoreId));

            var before =
                await ReadUserCredentialStateAsync(tenant.UserId);

            // Update the stamp through the scoped UserManager.
            await using (var identityScope =
                         stampFactory.Services.CreateAsyncScope())
            {
                var services = identityScope.ServiceProvider;

                var accessor = services
                    .GetRequiredService<IIdentityLookupScopeAccessor>();

                accessor.SetCurrent(
                    IdentityLookupScope.ForStoreCustomer(tenant.StoreId));

                var userManager = services
                    .GetRequiredService<UserManager<ApplicationUser>>();

                var user = await userManager.FindByIdAsync(tenant.UserId);

                Assert.NotNull(user);

                var result =
                    await userManager.UpdateSecurityStampAsync(user);

                Assert.True(result.Succeeded);
            }

            var after =
                await ReadUserCredentialStateAsync(tenant.UserId);

            Assert.NotEqual(before.SecurityStamp, after.SecurityStamp);

            // Existing SecurityStampValidator interval is five minutes.
            fakeTimeProvider.Advance(TimeSpan.FromMinutes(6));

            var basePath =
                $"/api/stores/{tenant.Slug}/customers";

            using var responseA = await SendAuthenticatedGetAsync(
                clientA,
                $"{basePath}/me",
                cookieA.CookieHeader);

            using var responseB = await SendAuthenticatedGetAsync(
                clientB,
                $"{basePath}/me",
                cookieB.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                responseA.StatusCode);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                responseB.StatusCode);

            // Each rejected cookie must remove its own server-side ticket.
            Assert.Equal(0, await CountSessionsAsync(tenant.StoreId));
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task ChangePassword_WithCrossStoreCookie_ReturnsForbidden_AndPreservesStoreASession()
    {
        var tenants = new List<TestTenant>();

        try
        {
            using var client = CreateStatelessClient();

            var storeA = await CreateTenantAsync("pw-change-store-a");
            tenants.Add(storeA);

            var storeB = await CreateTenantAsync("pw-change-store-b");
            tenants.Add(storeB);

            var cookieA = await LoginCustomerAsync(client, storeA);

            var before =
                await ReadUserCredentialStateAsync(storeA.UserId);

            var forgedCookieHeader = RemapCookieName(
                cookieA.CookieHeader,
                cookieA.Name,
                BuildCookieName(storeB.StoreId));

            using var forgedResponse = await SendBrowserPostAsync(
                client,
                $"/api/stores/{storeB.Slug}/customers/password/change",
                new
                {
                    CurrentPassword = TestPassword,
                    NewPassword = ChangedPassword
                },
                forgedCookieHeader);

            Assert.Equal(
                HttpStatusCode.Forbidden,
                forgedResponse.StatusCode);

            Assert.Empty(GetSetCookieHeaders(forgedResponse));

            var after =
                await ReadUserCredentialStateAsync(storeA.UserId);

            Assert.Equal(before.PasswordHash, after.PasswordHash);
            Assert.Equal(before.SecurityStamp, after.SecurityStamp);

            using var storeAMe = await SendAuthenticatedGetAsync(
                client,
                $"/api/stores/{storeA.Slug}/customers/me",
                cookieA.CookieHeader);

            Assert.Equal(HttpStatusCode.OK, storeAMe.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task ConcurrentLogin_DelayedUntilAfterPasswordChange_IsRejected_AndCreatesNoSession()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var tenant = await CreateTenantAsync("concurrent-login-after-change");
            tenants.Add(tenant);

            using var setupClient = CreateStatelessClient();
            var sessionCookie = await LoginCustomerAsync(setupClient, tenant);

            Assert.Equal(1, await CountSessionsAsync(tenant.StoreId));

            var loginEnteredTicketStore = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var allowLoginToContinue = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var hook = new DelegateStoreCustomerSessionOperationHook
            {
                BeforeTicketStoreTransaction = async (userId, ct) =>
                {
                    if (string.Equals(userId, tenant.UserId, StringComparison.Ordinal))
                    {
                        loginEnteredTicketStore.TrySetResult();
                        await allowLoginToContinue.Task.WaitAsync(ct);
                    }
                }
            };

            using var hookedFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IStoreCustomerSessionOperationHook>(hook);
                });
            });

            using var loginClient = CreateStatelessClient(hookedFactory);
            using var changeClient = CreateStatelessClient(hookedFactory);

            var basePath = $"/api/stores/{tenant.Slug}/customers";

            // Start Login with the old password; it will pause after password
            // verification and right before acquiring the SQL lock in StoreCoreAsync.
            var loginTask = SendBrowserPostAsync(
                loginClient,
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = TestPassword
                });

            await loginEnteredTicketStore.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Password change completes and commits while Login is paused.
            using var changeResponse = await SendBrowserPostAsync(
                changeClient,
                $"{basePath}/password/change",
                new
                {
                    CurrentPassword = TestPassword,
                    NewPassword = ChangedPassword
                },
                sessionCookie.CookieHeader);

            Assert.Equal(HttpStatusCode.NoContent, changeResponse.StatusCode);
            Assert.Equal(0, await CountSessionsAsync(tenant.StoreId));

            // Resume Login: SqlServerTicketStore must detect the updated SecurityStamp
            // under UPDLOCK and reject creating a stale session ticket.
            allowLoginToContinue.TrySetResult();

            using var loginResponse = await loginTask;

            Assert.Equal(HttpStatusCode.Unauthorized, loginResponse.StatusCode);
            Assert.False(loginResponse.Headers.TryGetValues("Set-Cookie", out _));
            Assert.Equal(0, await CountSessionsAsync(tenant.StoreId));
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task ConcurrentLogin_AcquiringUserLockFirst_WaitsForPasswordChangeToDeleteNewTicket()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var tenant = await CreateTenantAsync("concurrent-login-lock-first");
            tenants.Add(tenant);

            using var setupClient = CreateStatelessClient();
            var sessionCookie = await LoginCustomerAsync(setupClient, tenant);

            Assert.Equal(1, await CountSessionsAsync(tenant.StoreId));

            var loginLockedUserRow = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var allowLoginCommit = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var passwordChangeAttemptingLock = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var hook = new DelegateStoreCustomerSessionOperationHook
            {
                OnUserRowLocked = async (userId, cancellationToken) =>
                {
                    if (string.Equals(
                            userId,
                            tenant.UserId,
                            StringComparison.Ordinal))
                    {
                        loginLockedUserRow.TrySetResult();
                        await allowLoginCommit.Task.WaitAsync(
                            cancellationToken);
                    }
                },

                BeforePasswordChangeUserRowLock = (userId, _) =>
                {
                    if (string.Equals(
                            userId,
                            tenant.UserId,
                            StringComparison.Ordinal))
                    {
                        passwordChangeAttemptingLock.TrySetResult();
                    }

                    return Task.CompletedTask;
                }
            };

            using var hookedFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IStoreCustomerSessionOperationHook>(
                        hook);
                });
            });

            using var loginClient = CreateStatelessClient(hookedFactory);
            using var changeClient = CreateStatelessClient(hookedFactory);

            var basePath = $"/api/stores/{tenant.Slug}/customers";

            // -----------------------------------------------------
            // 1. Login acquires the SQL user-row lock and pauses.
            // -----------------------------------------------------

            var loginTask = SendBrowserPostAsync(
                loginClient,
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = TestPassword
                });

            await loginLockedUserRow.Task.WaitAsync(
                TimeSpan.FromSeconds(10));

            // -----------------------------------------------------
            // 2. Password Change starts while Login holds the lock.
            // -----------------------------------------------------

            var changeTask = SendBrowserPostAsync(
                changeClient,
                $"{basePath}/password/change",
                new
                {
                    CurrentPassword = TestPassword,
                    NewPassword = ChangedPassword
                },
                sessionCookie.CookieHeader);

            // Ensure Password Change reached the point immediately
            // before attempting its SQL user-row lock.
            await passwordChangeAttemptingLock.Task.WaitAsync(
                TimeSpan.FromSeconds(10));

            // -----------------------------------------------------
            // 3. Verify actual SQL Server blocking.
            // -----------------------------------------------------

            await WaitUntilPasswordChangeIsBlockedOnUserRowAsync(
                TimeSpan.FromSeconds(10));

            Assert.False(
                changeTask.IsCompleted,
                "Password Change completed while Login still held " +
                "the SQL user-row update lock.");

            // -----------------------------------------------------
            // 4. Release Login.
            // -----------------------------------------------------

            allowLoginCommit.TrySetResult();

            using var loginResponse = await loginTask;
            using var changeResponse = await changeTask;

            Assert.Equal(
                HttpStatusCode.NoContent,
                loginResponse.StatusCode);

            Assert.Equal(
                HttpStatusCode.NoContent,
                changeResponse.StatusCode);

            // -----------------------------------------------------
            // 5. Password Change must delete the ticket created by Login.
            // -----------------------------------------------------

            Assert.Equal(
                0,
                await CountSessionsAsync(tenant.StoreId));

            var racedCookie = ReadIssuedCookie(
                loginResponse,
                tenant.StoreId,
                tenant.Slug);

            using var meResponse = await SendAuthenticatedGetAsync(
                changeClient,
                $"{basePath}/me",
                racedCookie.CookieHeader);

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                meResponse.StatusCode);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    [Fact]
    public async Task ChangePassword_WhenTicketDeletionFails_RollsBackPasswordAndSecurityStamp_AndPreservesSessions()
    {
        var tenants = new List<TestTenant>();

        try
        {
            var tenant = await CreateTenantAsync("password-change-rollback");
            tenants.Add(tenant);

            using var clientA = CreateStatelessClient();
            using var clientB = CreateStatelessClient();

            var cookieA = await LoginCustomerAsync(clientA, tenant);
            var cookieB = await LoginCustomerAsync(clientB, tenant);

            Assert.Equal(2, await CountSessionsAsync(tenant.StoreId));

            var before =
                await ReadUserCredentialStateAsync(tenant.UserId);

            var deleteInterceptor = new FailTicketDeleteCommandInterceptor();

            using var faultFactory = _factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<DbCommandInterceptor>(
                        deleteInterceptor);
                });
            });

            using var faultClient = CreateStatelessClient(faultFactory);

            var basePath = $"/api/stores/{tenant.Slug}/customers";

            using var changeResponse = await SendBrowserPostAsync(
                faultClient,
                $"{basePath}/password/change",
                new
                {
                    CurrentPassword = TestPassword,
                    NewPassword = ChangedPassword
                },
                cookieA.CookieHeader);

            Assert.Equal(
                HttpStatusCode.InternalServerError,
                changeResponse.StatusCode);

            Assert.True(
                deleteInterceptor.WasTriggered,
                "The AuthenticationSessionTickets DELETE command " +
                "was not intercepted.");

            // PasswordHash and SecurityStamp must remain unchanged.
            var after =
                await ReadUserCredentialStateAsync(tenant.UserId);

            Assert.Equal(before.PasswordHash, after.PasswordHash);
            Assert.Equal(before.SecurityStamp, after.SecurityStamp);

            // Both original sessions must still exist.
            Assert.Equal(2, await CountSessionsAsync(tenant.StoreId));

            // Both original sessions must remain usable.
            using var meA = await SendAuthenticatedGetAsync(
                clientA,
                $"{basePath}/me",
                cookieA.CookieHeader);

            using var meB = await SendAuthenticatedGetAsync(
                clientB,
                $"{basePath}/me",
                cookieB.CookieHeader);

            Assert.Equal(HttpStatusCode.OK, meA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, meB.StatusCode);

            // New password must not have been committed.
            using var newPasswordAttempt = await SendBrowserPostAsync(
                clientA,
                $"{basePath}/login",
                new
                {
                    Email = tenant.Email,
                    Password = ChangedPassword
                });

            Assert.Equal(
                HttpStatusCode.Unauthorized,
                newPasswordAttempt.StatusCode);

            // Old password must still work.
            using var freshClient = CreateStatelessClient();
            _ = await LoginCustomerAsync(freshClient, tenant, TestPassword);
        }
        finally
        {
            await RemoveTestDataAsync(tenants);
        }
    }

    private async Task WaitUntilPasswordChangeIsBlockedOnUserRowAsync(
        TimeSpan timeout)
    {
        await using var scope = _factory.Services.CreateAsyncScope();

        var factory = scope.ServiceProvider
            .GetRequiredService<IDbContextFactory<SessionTicketDbContext>>();

        await using var db = await factory.CreateDbContextAsync();

        await db.Database.OpenConnectionAsync();

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandTimeout = 5;
        command.CommandText = """
            SELECT CASE WHEN EXISTS (
                SELECT 1
                FROM sys.dm_exec_requests AS request
                CROSS APPLY sys.dm_exec_sql_text(request.sql_handle) AS sqlText
                WHERE request.database_id = DB_ID()
                  AND request.session_id <> @@SPID
                  AND request.blocking_session_id > 0
                  AND request.wait_type LIKE N'LCK_M_%'
                  AND CHARINDEX(N'AspNetUsers', sqlText.text) > 0
                  AND CHARINDEX(N'UPDLOCK', sqlText.text) > 0
            )
            THEN 1 ELSE 0 END;
            """;

        var deadline = DateTimeOffset.UtcNow.Add(timeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await command.ExecuteScalarAsync();

            if (result is not null &&
                result != DBNull.Value &&
                Convert.ToInt32(result) == 1)
            {
                return;
            }

            // Polling interval only.
            // Success depends on observing actual SQL Server blocking,
            // not on elapsed time.
            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(
            "SQL Server did not report Password Change waiting " +
            "for the AspNetUsers UPDLOCK.");
    }

    private static HttpClient CreateStatelessClient(
        WebApplicationFactory<SessionTransportProgramMarker> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });
    }

    private sealed class DelegateStoreCustomerSessionOperationHook
        : IStoreCustomerSessionOperationHook
    {
        public Func<string, CancellationToken, Task>? BeforeTicketStoreTransaction { get; init; }

        public Func<string, CancellationToken, Task>? OnUserRowLocked { get; init; }

        public Func<string, CancellationToken, Task>? BeforePasswordChangeUserRowLock { get; init; }

        public Task OnBeforeTicketStoreTransactionAsync(
            string userId,
            CancellationToken cancellationToken)
            => BeforeTicketStoreTransaction?.Invoke(userId, cancellationToken)
               ?? Task.CompletedTask;

        public Task OnTicketStoreUserRowLockedAsync(
            string userId,
            CancellationToken cancellationToken)
            => OnUserRowLocked?.Invoke(userId, cancellationToken)
               ?? Task.CompletedTask;

        public Task OnBeforePasswordChangeUserRowLockAsync(
            string userId,
            CancellationToken cancellationToken)
            => BeforePasswordChangeUserRowLock?.Invoke(userId, cancellationToken)
               ?? Task.CompletedTask;
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

    private HttpClient CreateStatelessClient()
    {
        return _factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });
    }

    private async Task<PersistedAuthenticationTicket> ReadSingleSessionAsync(
        TestTenant tenant)
    {
        await using var scope =
            _factory.Services.CreateAsyncScope();

        var factory = scope.ServiceProvider
            .GetRequiredService<
                IDbContextFactory<SessionTicketDbContext>>();

        await using var db = await factory.CreateDbContextAsync();

        var sessions = await db.AuthenticationTickets
            .AsNoTracking()
            .Where(ticket =>
                ticket.UserId == tenant.UserId &&
                ticket.StoreId == tenant.StoreId)
            .ToListAsync();

        return Assert.Single(sessions);
    }

    private async Task<int> CountSessionsAsync(Guid storeId)
    {
        await using var scope =
            _factory.Services.CreateAsyncScope();

        var factory = scope.ServiceProvider
            .GetRequiredService<
                IDbContextFactory<SessionTicketDbContext>>();

        await using var db = await factory.CreateDbContextAsync();

        return await db.AuthenticationTickets
            .CountAsync(ticket => ticket.StoreId == storeId);
    }

    private async Task<IssuedCookie> LoginCustomerAsync(
        HttpClient client,
        TestTenant tenant,
        string password = TestPassword)
    {
        var basePath =
            $"/api/stores/{tenant.Slug}/customers";

        using var response = await SendBrowserPostAsync(
            client,
            $"{basePath}/login",
            new
            {
                Email = tenant.Email,
                Password = password
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        return ReadIssuedCookie(
            response,
            tenant.StoreId,
            tenant.Slug);
    }

    private async Task<UserCredentialState> ReadUserCredentialStateAsync(
        string userId)
    {
        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        var user = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == userId);

        return new UserCredentialState(
            user.PasswordHash,
            user.SecurityStamp);
    }

    private sealed record UserCredentialState(
        string? PasswordHash,
        string? SecurityStamp);

    private async Task<TestTenant> CreateCustomerForExistingStoreAsync(
        TestTenant existingTenant,
        string prefix)
    {
        var uniqueId = Guid.NewGuid().ToString("N");

        var tenant = new TestTenant(
            existingTenant.StoreId,
            existingTenant.Slug,
            $"customer-{uniqueId}@sessiontransport.invalid",
            $"customer-{uniqueId}");

        await using var identityScope =
            _factory.Services.CreateAsyncScope();

        var services = identityScope.ServiceProvider;

        var accessor = services
            .GetRequiredService<IIdentityLookupScopeAccessor>();

        accessor.SetCurrent(
            IdentityLookupScope.ForStoreCustomer(
                existingTenant.StoreId));

        var userManager = services
            .GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser
        {
            Id = tenant.UserId,
            UserName = tenant.UserName,
            Email = tenant.Email,
            EmailConfirmed = true,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = existingTenant.StoreId,
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
                $"Could not create the additional test customer ({prefix}): {errors}");
        }

        return tenant;
    }

    private static AuthenticationTicket CreateTicketWithTimes(
        AuthenticationTicket source,
        DateTimeOffset issuedUtc,
        DateTimeOffset expiresUtc)
    {
        var properties = new AuthenticationProperties();

        foreach (var item in source.Properties.Items)
        {
            properties.Items[item.Key] = item.Value;
        }

        properties.IssuedUtc = issuedUtc;
        properties.ExpiresUtc = expiresUtc;
        properties.IsPersistent = source.Properties.IsPersistent;
        properties.AllowRefresh = source.Properties.AllowRefresh;
        properties.RedirectUri = source.Properties.RedirectUri;

        return new AuthenticationTicket(
            source.Principal,
            properties,
            source.AuthenticationScheme);
    }

    private static async Task<HttpResponseMessage> SendBrowserPostAsync(
        HttpClient client,
        string path,
        object payload,
        string? cookieHeader = null)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            path)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.TryAddWithoutValidation(
            "Origin",
            "https://localhost");

        request.Headers.TryAddWithoutValidation(
            "X-RetailFlow-Request",
            "1");

        request.Headers.TryAddWithoutValidation(
            "Sec-Fetch-Site",
            "same-origin");

        if (!string.IsNullOrWhiteSpace(cookieHeader))
        {
            request.Headers.TryAddWithoutValidation(
                "Cookie",
                cookieHeader);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendAuthenticatedGetAsync(
        HttpClient client,
        string path,
        string cookieHeader)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            path);

        request.Headers.TryAddWithoutValidation(
            "Cookie",
            cookieHeader);

        return await client.SendAsync(request);
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
