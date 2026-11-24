using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class BackOfficeRouteGroupIntegrationTests
{
    [Fact]
    public async Task BackOfficeRouteGroup_EstablishesGlobalScopeWithoutResolvingStore()
    {
        // Arrange
        var resolver = new SpyIdentityScopeResolver();

        using var factory = new IdentitySpikeApiFactory(services =>
        {
            services.RemoveAll<IIdentityScopeResolver>();
            services.AddSingleton<IIdentityScopeResolver>(resolver);
        });

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

        // Act
        using var response = await client.GetAsync(
            "/api/backoffice/scope");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<ScopeResponse>();

        Assert.NotNull(payload);
        Assert.Equal("BackOffice", payload.AccountKind);
        Assert.Null(payload.StoreId);

        // BackOffice scope must not resolve a store.
        Assert.Equal(0, resolver.CallCount);
    }

    private sealed record ScopeResponse(
        Guid? StoreId,
        string AccountKind);

    private sealed class SpyIdentityScopeResolver
        : IIdentityScopeResolver
    {
        public int CallCount { get; private set; }

        public Task<Guid?> ResolveActiveStoreIdAsync(
            string slug,
            CancellationToken cancellationToken)
        {
            CallCount++;

            throw new InvalidOperationException(
                "BackOffice scope must not resolve a store.");
        }
    }
}
