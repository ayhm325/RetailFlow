using System.Text.Json;

using Microsoft.Playwright;

using RetailFlow.SessionTransportSpike.Authentication;
using RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Authentication;

public sealed class BrowserCsrfAcceptanceTests
    : IClassFixture<PlaywrightApiFixture>
{
    private readonly PlaywrightApiFixture _fixture;

    public BrowserCsrfAcceptanceTests(PlaywrightApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CrossSiteLoginFormPost_IsRejectedBeforeIssuingAuthenticationCookie()
    {
        var seededTenants = new List<SeededTenant>();

        await using var attacker = await BrowserAttackerServer.StartAsync();
        await using var context = await _fixture.CreateBrowserContextAsync();

        try
        {
            await _fixture.ExecuteWithDiagnosticsAsync(
                context,
                nameof(CrossSiteLoginFormPost_IsRejectedBeforeIssuingAuthenticationCookie),
                async () =>
                {
                    var storeA = await _fixture.Seeder.SeedStoreCustomerAsync(
                        "pw-csrf-login");
                    seededTenants.Add(storeA);

                    var page = await context.NewPageAsync();

                    var loginUrl =
                        $"{_fixture.BaseUrl}/api/stores/{storeA.Slug}/customers/login";

                    var attackUrl =
                        $"{attacker.Origin}/attack-login" +
                        $"?action={Uri.EscapeDataString(loginUrl)}" +
                        $"&email={Uri.EscapeDataString(storeA.Email)}" +
                        $"&password={Uri.EscapeDataString(storeA.Password)}";

                    var response = await page.RunAndWaitForResponseAsync(
                        () => page.GotoAsync(attackUrl),
                        r => string.Equals(
                                 r.Url,
                                 loginUrl,
                                 StringComparison.Ordinal) &&
                             string.Equals(
                                 r.Request.Method,
                                 "POST",
                                 StringComparison.OrdinalIgnoreCase));

                    Assert.Equal("POST", response.Request.Method);
                    Assert.Equal(403, response.Status);

                    var requestHeaders = await response.Request.AllHeadersAsync();

                    Assert.True(
                        requestHeaders.TryGetValue("origin", out var originHeader),
                        "Expected browser to send Origin header on cross-site POST.");
                    Assert.Equal(attacker.Origin, originHeader);

                    Assert.True(
                        requestHeaders.TryGetValue(
                            "sec-fetch-site",
                            out var secFetchSite),
                        "Expected browser to send Sec-Fetch-Site header.");
                    Assert.Equal("cross-site", secFetchSite);

                    Assert.False(
                        requestHeaders.ContainsKey("x-retailflow-request"));

                    var responseHeaders = await response.AllHeadersAsync();
                    Assert.False(responseHeaders.ContainsKey("set-cookie"));

                    var cookies = await context.CookiesAsync();
                    Assert.DoesNotContain(
                        cookies,
                        c => c.Name.StartsWith(
                            StoreCustomerCookieManager.BaseCookieName,
                            StringComparison.Ordinal));
                });
        }
        finally
        {
            await _fixture.Seeder.CleanupAsync(seededTenants);
        }
    }

    [Fact]
    public async Task CrossSiteLogoutFormPost_IsRejectedAndExistingSessionRemainsValid()
    {
        var seededTenants = new List<SeededTenant>();

        await using var attacker = await BrowserAttackerServer.StartAsync();
        await using var context = await _fixture.CreateBrowserContextAsync();

        try
        {
            await _fixture.ExecuteWithDiagnosticsAsync(
                context,
                nameof(CrossSiteLogoutFormPost_IsRejectedAndExistingSessionRemainsValid),
                async () =>
                {
                    var storeA = await _fixture.Seeder.SeedStoreCustomerAsync(
                        "pw-csrf-logout");
                    seededTenants.Add(storeA);

                    var page = await context.NewPageAsync();
                    await page.GotoAsync(_fixture.BaseUrl);

                    // 1. Legitimate same-origin browser login.
                    var loginStatus = await page.EvaluateAsync<int>(
                        """
                        async ([url, bodyJson]) => {
                            const response = await fetch(url, {
                                method: "POST",
                                credentials: "include",
                                headers: {
                                    "Content-Type": "application/json",
                                    "X-RetailFlow-Request": "1"
                                },
                                body: bodyJson
                            });
                            return response.status;
                        }
                        """,
                        new object[]
                        {
                            $"/api/stores/{storeA.Slug}/customers/login",
                            JsonSerializer.Serialize(new
                            {
                                email = storeA.Email,
                                password = storeA.Password
                            })
                        });

                    Assert.Equal(204, loginStatus);

                    // 2. Verify /me returns 200 before the cross-site attack.
                    var mePath = $"/api/stores/{storeA.Slug}/customers/me";

                    var meBeforeStatus = await page.EvaluateAsync<int>(
                        """
                        async (url) => {
                            const response = await fetch(url, {
                                method: "GET",
                                credentials: "include"
                            });
                            return response.status;
                        }
                        """,
                        mePath);

                    Assert.Equal(200, meBeforeStatus);

                    // 3. Navigate to external attacker origin and submit cross-site logout POST.
                    var logoutUrl =
                        $"{_fixture.BaseUrl}/api/stores/{storeA.Slug}/customers/logout";

                    var attackUrl =
                        $"{attacker.Origin}/attack-logout" +
                        $"?action={Uri.EscapeDataString(logoutUrl)}";

                    var attackResponse = await page.RunAndWaitForResponseAsync(
                        () => page.GotoAsync(attackUrl),
                        r => string.Equals(
                                 r.Url,
                                 logoutUrl,
                                 StringComparison.Ordinal) &&
                             string.Equals(
                                 r.Request.Method,
                                 "POST",
                                 StringComparison.OrdinalIgnoreCase));

                    var requestHeaders =
                        await attackResponse.Request.AllHeadersAsync();

                    Assert.True(
                        requestHeaders.TryGetValue(
                            "sec-fetch-site",
                            out var secFetchSite));
                    Assert.Equal("cross-site", secFetchSite);

                    Assert.False(
                        requestHeaders.ContainsKey("x-retailflow-request"));

                    Assert.Equal(403, attackResponse.Status);

                    var responseHeaders = await attackResponse.AllHeadersAsync();
                    Assert.False(responseHeaders.ContainsKey("set-cookie"));

                    // 4. Return to API origin and confirm the legitimate session is still 200.
                    await page.GotoAsync(_fixture.BaseUrl);

                    var meAfterStatus = await page.EvaluateAsync<int>(
                        """
                        async (url) => {
                            const response = await fetch(url, {
                                method: "GET",
                                credentials: "include"
                            });
                            return response.status;
                        }
                        """,
                        mePath);

                    Assert.Equal(200, meAfterStatus);
                });
        }
        finally
        {
            await _fixture.Seeder.CleanupAsync(seededTenants);
        }
    }
}
