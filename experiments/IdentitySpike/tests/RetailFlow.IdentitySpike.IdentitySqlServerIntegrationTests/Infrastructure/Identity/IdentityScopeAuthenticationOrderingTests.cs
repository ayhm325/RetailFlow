using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentityScopeAuthenticationOrderingTests
{
    [Fact]
    public async Task AuthenticationHandler_SeesScopeEstablishedBeforeAuthentication()
    {
        var expectedStoreId = Guid.NewGuid();
        var probe = new AuthenticationScopeProbe();

        using var host = await CreateHostAsync(
            expectedStoreId,
            probe);

        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/stores/store-a/customers/secure");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(1, probe.CallCount);
        Assert.NotNull(probe.ObservedScope);
        Assert.Equal(
            AccountKind.StoreCustomer,
            probe.ObservedScope.AccountKind);
        Assert.Equal(
            expectedStoreId,
            probe.ObservedScope.StoreId);
    }

    private static async Task<IHost> CreateHostAsync(
        Guid storeId,
        AuthenticationScopeProbe probe)
    {
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseTestServer();

                webBuilder.ConfigureServices(services =>
                {
                    services.AddRouting();

                    services.AddScoped<
                        IIdentityLookupScopeAccessor,
                        IdentityLookupScopeAccessor>();

                    services.AddSingleton<IIdentityScopeResolver>(
                        new FixedIdentityScopeResolver(storeId));

                    services.AddSingleton(probe);

                    services
                        .AddAuthentication(options =>
                        {
                            options.DefaultAuthenticateScheme = "ScopeTest";
                            options.DefaultChallengeScheme = "ScopeTest";
                        })
                        .AddScheme<
                            AuthenticationSchemeOptions,
                            ScopeObservingAuthenticationHandler>(
                            "ScopeTest",
                            _ => { });

                    services.AddAuthorization();
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();

                    app.UseMiddleware<IdentityScopeMiddleware>();

                    app.UseAuthentication();
                    app.UseAuthorization();

                    app.UseEndpoints(endpoints =>
                    {
                        var storeGroup = endpoints
                            .MapGroup("/stores/{slug}/customers")
                            .WithMetadata(
                                new IdentityScopeMetadata(
                                    AccountKind.StoreCustomer));

                        storeGroup
                            .MapGet("/secure", () => Results.Ok())
                            .RequireAuthorization();
                    });
                });
            })
            .StartAsync();
    }

    private sealed class FixedIdentityScopeResolver
        : IIdentityScopeResolver
    {
        private readonly Guid _storeId;

        public FixedIdentityScopeResolver(Guid storeId)
        {
            _storeId = storeId;
        }

        public Task<Guid?> ResolveActiveStoreIdAsync(
            string slug,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<Guid?>(_storeId);
        }
    }
}

public sealed class AuthenticationScopeProbe
{
    public int CallCount { get; private set; }

    public IdentityLookupScope? ObservedScope { get; private set; }

    public void Capture(IdentityLookupScope? scope)
    {
        CallCount++;
        ObservedScope = scope;
    }
}

public sealed class ScopeObservingAuthenticationHandler
    : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public ScopeObservingAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var requestServices = Context.RequestServices;

        var accessor = requestServices
            .GetRequiredService<IIdentityLookupScopeAccessor>();

        var probe = requestServices
            .GetRequiredService<AuthenticationScopeProbe>();

        probe.Capture(accessor.Current);

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                "scope-order-test")
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(
            principal,
            Scheme.Name);

        return Task.FromResult(
            AuthenticateResult.Success(ticket));
    }
}
