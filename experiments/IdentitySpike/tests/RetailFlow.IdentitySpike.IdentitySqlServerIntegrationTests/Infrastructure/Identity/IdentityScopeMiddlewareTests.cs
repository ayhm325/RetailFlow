using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentityScopeMiddlewareTests
{
    [Fact]
    public async Task StoreCustomerEndpoint_ResolvesStoreAndSetsScope()
    {
        var expectedStoreId = Guid.NewGuid();
        var resolver = new StubIdentityScopeResolver(expectedStoreId);

        using var host = await CreateHostAsync(resolver);
        using var client = host.GetTestClient();

        var response = await client.GetAsync(
            "/stores/IRBID/customers/scope");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<ScopePayload>();

        Assert.NotNull(payload);
        Assert.True(payload.HasScope);
        Assert.Equal("StoreCustomer", payload.AccountKind);
        Assert.Equal(expectedStoreId, payload.StoreId);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("IRBID", resolver.LastSlug);
    }

    [Fact]
    public async Task BackOfficeEndpoint_SetsGlobalScopeWithoutResolvingStore()
    {
        var resolver = new StubIdentityScopeResolver(Guid.NewGuid());

        using var host = await CreateHostAsync(resolver);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/backoffice/scope");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<ScopePayload>();

        Assert.NotNull(payload);
        Assert.True(payload.HasScope);
        Assert.Equal("BackOffice", payload.AccountKind);
        Assert.Null(payload.StoreId);

        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task EndpointWithoutMetadata_DoesNotEstablishScope()
    {
        var resolver = new StubIdentityScopeResolver(Guid.NewGuid());

        using var host = await CreateHostAsync(resolver);
        using var client = host.GetTestClient();

        var response = await client.GetAsync("/public");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content
            .ReadFromJsonAsync<ScopePayload>();

        Assert.NotNull(payload);
        Assert.False(payload.HasScope);
        Assert.Null(payload.AccountKind);
        Assert.Null(payload.StoreId);

        Assert.Equal(0, resolver.CallCount);
    }

    [Fact]
    public async Task StoreCustomerEndpoint_ReturnsNotFoundWhenStoreCannotBeResolved()
    {
        var resolver = new StubIdentityScopeResolver(_ => null);

        using var host = await CreateHostAsync(resolver);
        using var client = host.GetTestClient();

        using var response = await client.GetAsync(
            "/stores/unknown-store/customers/scope");

        Assert.Equal(
            HttpStatusCode.NotFound,
            response.StatusCode);

        Assert.Equal(1, resolver.CallCount);
        Assert.Equal("unknown-store", resolver.LastSlug);
    }

    [Fact]
    public async Task SequentialStoreRequests_ReceiveIndependentScopes()
    {
        var storeAId = Guid.NewGuid();
        var storeBId = Guid.NewGuid();

        var resolver = new StubIdentityScopeResolver(
            slug => slug switch
            {
                "store-a" => storeAId,
                "store-b" => storeBId,
                _ => null
            });

        using var host = await CreateHostAsync(resolver);
        using var client = host.GetTestClient();

        // First HTTP request: Store A.
        using var responseA = await client.GetAsync(
            "/stores/store-a/customers/scope");

        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);

        var payloadA = await responseA.Content
            .ReadFromJsonAsync<ScopePayload>();

        Assert.NotNull(payloadA);
        Assert.True(payloadA.HasScope);
        Assert.Equal(storeAId, payloadA.StoreId);

        // Second HTTP request: Store B.
        using var responseB = await client.GetAsync(
            "/stores/store-b/customers/scope");

        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

        var payloadB = await responseB.Content
            .ReadFromJsonAsync<ScopePayload>();

        Assert.NotNull(payloadB);
        Assert.True(payloadB.HasScope);
        Assert.Equal(storeBId, payloadB.StoreId);

        Assert.NotEqual(payloadA.StoreId, payloadB.StoreId);

        Assert.Equal(2, resolver.CallCount);
    }

    [Fact]
    public async Task EndpointWithoutIdentityMetadata_ContinuesWithoutEstablishingScope()
    {
        // Arrange
        var context = new DefaultHttpContext();

        context.SetEndpoint(
            new Endpoint(
                requestDelegate: _ => Task.CompletedTask,
                metadata: new EndpointMetadataCollection(),
                displayName: "Public Endpoint"));

        var accessor = new IdentityLookupScopeAccessor();
        var resolver = new SpyIdentityScopeResolver();

        var nextCalled = false;

        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new IdentityScopeMiddleware(next);

        // Act
        await middleware.InvokeAsync(
            context,
            resolver,
            accessor);

        // Assert
        Assert.True(nextCalled);
        Assert.Equal(0, resolver.CallCount);
        Assert.Null(accessor.Current);
    }

    public static TheoryData<bool, object?> InvalidSlugCases =>
        new()
        {
            // Route value does not exist.
            { false, null },

            // Route value exists but is null.
            { true, null },

            // Empty slug.
            { true, string.Empty },

            // Whitespace-only slug.
            { true, " " },

            // Route value has an unexpected type.
            { true, 123 }
        };

    [Theory]
    [MemberData(nameof(InvalidSlugCases))]
    public async Task StoreCustomerEndpoint_MissingOrInvalidSlug_ReturnsNotFound(
        bool hasRouteValue,
        object? slugValue)
    {
        // Arrange
        var context = new DefaultHttpContext();

        context.SetEndpoint(
            new Endpoint(
                requestDelegate: _ => Task.CompletedTask,
                metadata: new EndpointMetadataCollection(
                    new object[]
                    {
                        new IdentityScopeMetadata(
                            AccountKind.StoreCustomer)
                    }),
                displayName: "Store Customer Endpoint"));

        if (hasRouteValue)
        {
            context.Request.RouteValues["slug"] = slugValue!;
        }

        var accessor = new IdentityLookupScopeAccessor();
        var resolver = new SpyIdentityScopeResolver();

        var nextCalled = false;

        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = new IdentityScopeMiddleware(next);

        // Act
        await middleware.InvokeAsync(
            context,
            resolver,
            accessor);

        // Assert
        Assert.Equal(
            StatusCodes.Status404NotFound,
            context.Response.StatusCode);

        Assert.False(nextCalled);
        Assert.Equal(0, resolver.CallCount);
        Assert.Null(accessor.Current);
    }

    private static async Task<IHost> CreateHostAsync(
        StubIdentityScopeResolver resolver)
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
                        resolver);
                });

                webBuilder.Configure(app =>
                {
                    app.UseRouting();

                    app.UseMiddleware<IdentityScopeMiddleware>();

                    app.UseEndpoints(endpoints =>
                    {
                        var storeGroup = endpoints
                            .MapGroup("/stores/{slug}/customers")
                            .WithMetadata(
                                new IdentityScopeMetadata(
                                    AccountKind.StoreCustomer));

                        storeGroup.MapGet(
                            "/scope",
                            GetScope);

                        var backOfficeGroup = endpoints
                            .MapGroup("/backoffice")
                            .WithMetadata(
                                new IdentityScopeMetadata(
                                    AccountKind.BackOffice));

                        backOfficeGroup.MapGet(
                            "/scope",
                            GetScope);

                        endpoints.MapGet("/public", GetScope);
                    });
                });
            })
            .StartAsync();
    }

    private static IResult GetScope(
        IIdentityLookupScopeAccessor accessor)
    {
        var scope = accessor.Current;

        return Results.Json(new ScopePayload(
            HasScope: scope is not null,
            AccountKind: scope?.AccountKind.ToString(),
            StoreId: scope?.StoreId));
    }

    public sealed record ScopePayload(
        bool HasScope,
        string? AccountKind,
        Guid? StoreId);

    private sealed class StubIdentityScopeResolver
        : IIdentityScopeResolver
    {
        private readonly Func<string, Guid?> _resolveStoreId;

        public int CallCount { get; private set; }

        public string? LastSlug { get; private set; }

        public StubIdentityScopeResolver(Guid? storeId)
            : this(_ => storeId)
        {
        }

        public StubIdentityScopeResolver(
            Func<string, Guid?> resolveStoreId)
        {
            _resolveStoreId = resolveStoreId
                ?? throw new ArgumentNullException(nameof(resolveStoreId));
        }

        public Task<Guid?> ResolveActiveStoreIdAsync(
            string slug,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            LastSlug = slug;

            return Task.FromResult(_resolveStoreId(slug));
        }
    }

    private sealed class SpyIdentityScopeResolver : IIdentityScopeResolver
    {
        public int CallCount { get; private set; }

        public Task<Guid?> ResolveActiveStoreIdAsync(
            string slug,
            CancellationToken cancellationToken)
        {
            CallCount++;

            throw new InvalidOperationException(
                "Resolver should not be called.");
        }
    }
}
