using System.Text.Json;

using Microsoft.Playwright;

using RetailFlow.SessionTransportSpike.Authentication;
using RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Authentication;

public sealed class StoreCustomerBrowserSessionTests
    : IClassFixture<PlaywrightApiFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly PlaywrightApiFixture _fixture;

    public StoreCustomerBrowserSessionTests(PlaywrightApiFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task StoreAAndStoreB_CanMaintainSeparateSessionsInTwoTabs_AndLogoutIsIsolated()
    {
        var seededTenants = new List<SeededTenant>();

        await using var context = await _fixture.CreateBrowserContextAsync();

        try
        {
            await _fixture.ExecuteWithDiagnosticsAsync(
                context,
                nameof(StoreAAndStoreB_CanMaintainSeparateSessionsInTwoTabs_AndLogoutIsIsolated),
                async () =>
                {
                    var storeA = await _fixture.Seeder.SeedStoreCustomerAsync(
                        "pw-store-a");
                    seededTenants.Add(storeA);

                    var storeB = await _fixture.Seeder.SeedStoreCustomerAsync(
                        "pw-store-b");
                    seededTenants.Add(storeB);

                    var pageA = await context.NewPageAsync();
                    var pageB = await context.NewPageAsync();

                    await pageA.GotoAsync(_fixture.BaseUrl);
                    await pageB.GotoAsync(_fixture.BaseUrl);

                    var storeALogin = await BrowserPostJsonAsync(
                        pageA,
                        $"/api/stores/{storeA.Slug}/customers/login",
                        new { email = storeA.Email, password = storeA.Password });

                    Assert.Equal(204, storeALogin.Status);

                    var storeBLogin = await BrowserPostJsonAsync(
                        pageB,
                        $"/api/stores/{storeB.Slug}/customers/login",
                        new { email = storeB.Email, password = storeB.Password });

                    Assert.Equal(204, storeBLogin.Status);

                    var cookies = await context.CookiesAsync();

                    var expectedNameA =
                        $"{StoreCustomerCookieManager.BaseCookieName}.store.{storeA.StoreId:N}";
                    var expectedNameB =
                        $"{StoreCustomerCookieManager.BaseCookieName}.store.{storeB.StoreId:N}";

                    var expectedPathA =
                        $"/api/stores/{storeA.Slug}/customers";
                    var expectedPathB =
                        $"/api/stores/{storeB.Slug}/customers";

                    var cookieA = Assert.Single(
                        cookies,
                        c => string.Equals(
                            c.Name,
                            expectedNameA,
                            StringComparison.Ordinal));

                    var cookieB = Assert.Single(
                        cookies,
                        c => string.Equals(
                            c.Name,
                            expectedNameB,
                            StringComparison.Ordinal));

                    Assert.NotEqual(cookieA.Name, cookieB.Name);
                    Assert.NotEqual(cookieA.Path, cookieB.Path);

                    Assert.Equal(expectedPathA, cookieA.Path);
                    Assert.True(cookieA.Secure);
                    Assert.True(cookieA.HttpOnly);
                    Assert.Equal(SameSiteAttribute.Lax, cookieA.SameSite);

                    Assert.Equal(expectedPathB, cookieB.Path);
                    Assert.True(cookieB.Secure);
                    Assert.True(cookieB.HttpOnly);
                    Assert.Equal(SameSiteAttribute.Lax, cookieB.SameSite);

                    // Verify /me from Page A and Page B in the same BrowserContext.
                    var meAResponse = await BrowserGetAsync(
                        pageA,
                        $"{expectedPathA}/me");

                    Assert.Equal(200, meAResponse.Status);

                    var meA = JsonSerializer.Deserialize<MePayload>(
                        meAResponse.Body,
                        JsonOptions);

                    Assert.NotNull(meA);
                    Assert.Equal(storeA.UserId, meA.UserId);
                    Assert.Equal("StoreCustomer", meA.AccountKind);
                    Assert.Equal(storeA.StoreId.ToString("D"), meA.StoreId);

                    var meBResponse = await BrowserGetAsync(
                        pageB,
                        $"{expectedPathB}/me");

                    Assert.Equal(200, meBResponse.Status);

                    var meB = JsonSerializer.Deserialize<MePayload>(
                        meBResponse.Body,
                        JsonOptions);

                    Assert.NotNull(meB);
                    Assert.Equal(storeB.UserId, meB.UserId);
                    Assert.Equal("StoreCustomer", meB.AccountKind);
                    Assert.Equal(storeB.StoreId.ToString("D"), meB.StoreId);

                    // Logout Store A from Page A with required CSRF header.
                    var logoutAResponse = await BrowserPostEmptyAsync(
                        pageA,
                        $"{expectedPathA}/logout");

                    Assert.Equal(204, logoutAResponse.Status);

                    // Inspect Cookie Jar again: Store A removed, Store B preserved.
                    var cookiesAfterLogout =
                        await context.CookiesAsync();

                    Assert.DoesNotContain(
                        cookiesAfterLogout,
                        c => string.Equals(
                            c.Name,
                            expectedNameA,
                            StringComparison.Ordinal));

                    Assert.Contains(
                        cookiesAfterLogout,
                        c => string.Equals(
                            c.Name,
                            expectedNameB,
                            StringComparison.Ordinal));

                    // Store A /me must now return 401.
                    var meAAfterLogout = await BrowserGetAsync(
                        pageA,
                        $"{expectedPathA}/me");

                    Assert.Equal(401, meAAfterLogout.Status);

                    // Store B /me must still return 200 with Store B identity.
                    var meBAfterLogout = await BrowserGetAsync(
                        pageB,
                        $"{expectedPathB}/me");

                    Assert.Equal(200, meBAfterLogout.Status);

                    var meBAfter = JsonSerializer.Deserialize<MePayload>(
                        meBAfterLogout.Body,
                        JsonOptions);

                    Assert.NotNull(meBAfter);
                    Assert.Equal(storeB.UserId, meBAfter.UserId);
                    Assert.Equal(
                        storeB.StoreId.ToString("D"),
                        meBAfter.StoreId);

                    // Shared SQL DataProtectionKeys must remain untouched by Testing runs.
                    Assert.Equal(
                        _fixture.InitialDataProtectionKeysCount,
                        await _fixture.Seeder.CountDataProtectionKeysAsync());
                });
        }
        finally
        {
            await _fixture.Seeder.CleanupAsync(seededTenants);
        }
    }

    private static async Task<BrowserFetchResult> BrowserPostJsonAsync(
        IPage page,
        string path,
        object payload)
    {
        var json = JsonSerializer.Serialize(payload);

        return await page.EvaluateAsync<BrowserFetchResult>(
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
                return {
                    status: response.status,
                    body: await response.text()
                };
            }
            """,
            new object[] { path, json });
    }

    private static async Task<BrowserFetchResult> BrowserPostEmptyAsync(
        IPage page,
        string path)
    {
        return await page.EvaluateAsync<BrowserFetchResult>(
            """
            async (url) => {
                const response = await fetch(url, {
                    method: "POST",
                    credentials: "include",
                    headers: {
                        "X-RetailFlow-Request": "1"
                    }
                });
                return {
                    status: response.status,
                    body: await response.text()
                };
            }
            """,
            path);
    }

    private static async Task<BrowserFetchResult> BrowserGetAsync(
        IPage page,
        string path)
    {
        return await page.EvaluateAsync<BrowserFetchResult>(
            """
            async (url) => {
                const response = await fetch(url, {
                    method: "GET",
                    credentials: "include"
                });
                return {
                    status: response.status,
                    body: await response.text()
                };
            }
            """,
            path);
    }

    private sealed class BrowserFetchResult
    {
        public int Status { get; set; }

        public string Body { get; set; } = string.Empty;
    }

    private sealed record MePayload(
        string? UserId,
        string? AccountKind,
        string? StoreId);
}
