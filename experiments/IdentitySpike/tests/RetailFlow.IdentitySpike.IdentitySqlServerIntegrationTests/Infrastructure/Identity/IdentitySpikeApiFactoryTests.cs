using System.Net;
using Microsoft.Extensions.DependencyInjection;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentitySpikeApiFactoryTests
    : IClassFixture<IdentitySpikeApiFactory>
{
    private readonly IdentitySpikeApiFactory _factory;

    public IdentitySpikeApiFactoryTests(
        IdentitySpikeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ApiHost_UsesConfiguredSqlServerConnection()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(
            "/diagnostics/database");

        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
    }

    [Fact]
    public void IdentityScopeServices_AreScopedPerRequest()
    {
        using var firstScope = _factory.Services.CreateScope();
        using var secondScope = _factory.Services.CreateScope();

        var firstResolver = firstScope.ServiceProvider
            .GetRequiredService<IIdentityScopeResolver>();

        var secondResolver = secondScope.ServiceProvider
            .GetRequiredService<IIdentityScopeResolver>();

        var firstAccessor = firstScope.ServiceProvider
            .GetRequiredService<IIdentityLookupScopeAccessor>();

        var secondAccessor = secondScope.ServiceProvider
            .GetRequiredService<IIdentityLookupScopeAccessor>();

        Assert.Same(
            firstResolver,
            firstScope.ServiceProvider
                .GetRequiredService<IIdentityScopeResolver>());

        Assert.Same(
            firstAccessor,
            firstScope.ServiceProvider
                .GetRequiredService<IIdentityLookupScopeAccessor>());

        Assert.NotSame(firstResolver, secondResolver);
        Assert.NotSame(firstAccessor, secondAccessor);
    }
}
