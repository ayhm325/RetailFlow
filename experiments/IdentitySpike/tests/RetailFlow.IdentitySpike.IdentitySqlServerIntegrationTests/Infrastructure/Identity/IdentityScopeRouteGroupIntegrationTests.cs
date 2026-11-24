using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentityScopeRouteGroupIntegrationTests
    : IClassFixture<IdentitySpikeApiFactory>
{
    private readonly IdentitySpikeApiFactory _factory;

    public IdentityScopeRouteGroupIntegrationTests(
        IdentitySpikeApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task StoreCustomerRouteGroup_ResolvesRealStoreScope()
    {
        // Arrange: Create a real store in SQL Server.
        var storeId = Guid.NewGuid();
        var uniqueSlug = $"scope-test-{Guid.NewGuid():N}";

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        var store = new SpikeStore
        {
            Id = storeId,
            Name = "Identity Scope Integration Test",
            Slug = uniqueSlug,
            IsActive = true
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing
                    .WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost")
                });

            // Act: Send a real HTTP request through Program.cs.
            using var response = await client.GetAsync(
                $"/api/stores/{uniqueSlug}/customers/scope");

            // Assert: The actual endpoint must resolve the SQL store.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var payload = await response.Content
                .ReadFromJsonAsync<ScopeResponse>();

            Assert.NotNull(payload);
            Assert.Equal(storeId, payload.StoreId);
            Assert.Equal("StoreCustomer", payload.AccountKind);
        }
        finally
        {
            // Cleanup the SQL test data.
            db.Stores.Remove(store);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task StoreCustomerRouteGroup_UnknownStore_ReturnsNotFound()
    {
        // Arrange
        var unknownSlug = $"unknown-{Guid.NewGuid():N}";

        using var client = _factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing
                .WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

        // Act
        using var response = await client.GetAsync(
            $"/api/stores/{unknownSlug}/customers/scope");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task StoreCustomerRouteGroup_InactiveStore_ReturnsNotFound()
    {
        // Arrange
        var storeId = Guid.NewGuid();
        var uniqueSlug = $"inactive-{Guid.NewGuid():N}";

        await using var scope = _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        var store = new SpikeStore
        {
            Id = storeId,
            Name = "Inactive Identity Scope Test",
            Slug = uniqueSlug,
            IsActive = false
        };

        db.Stores.Add(store);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing
                    .WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost")
                });

            // Act
            using var response = await client.GetAsync(
                $"/api/stores/{uniqueSlug}/customers/scope");

            // Assert
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        finally
        {
            db.Stores.Remove(store);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task StoreCustomerRouteGroup_TwoStores_HaveIsolatedScopes()
    {
        // Arrange
        var storeA = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = "Isolation Store A",
            Slug = $"isolation-a-{Guid.NewGuid():N}",
            IsActive = true
        };

        var storeB = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = "Isolation Store B",
            Slug = $"isolation-b-{Guid.NewGuid():N}",
            IsActive = true
        };

        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing
                    .WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost")
                });

            // Act: send both requests concurrently.
            var requestA = client.GetAsync(
                $"/api/stores/{storeA.Slug}/customers/scope");

            var requestB = client.GetAsync(
                $"/api/stores/{storeB.Slug}/customers/scope");

            using var responseA = await requestA;
            using var responseB = await requestB;

            // Assert: both requests succeed.
            Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
            Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

            var resultA = await responseA.Content
                .ReadFromJsonAsync<ScopeResponse>();

            var resultB = await responseB.Content
                .ReadFromJsonAsync<ScopeResponse>();

            Assert.NotNull(resultA);
            Assert.NotNull(resultB);

            // Each request must resolve its own store.
            Assert.Equal(storeA.Id, resultA.StoreId);
            Assert.Equal(storeB.Id, resultB.StoreId);

            Assert.NotEqual(resultA.StoreId, resultB.StoreId);

            Assert.Equal("StoreCustomer", resultA.AccountKind);
            Assert.Equal("StoreCustomer", resultB.AccountKind);
        }
        finally
        {
            db.Stores.RemoveRange(storeA, storeB);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task StoreCustomerRouteGroup_SequentialRequests_HaveIndependentScopes()
    {
        // Arrange
        var storeA = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = "Lifecycle Store A",
            Slug = $"lifecycle-a-{Guid.NewGuid():N}",
            IsActive = true
        };

        var storeB = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = "Lifecycle Store B",
            Slug = $"lifecycle-b-{Guid.NewGuid():N}",
            IsActive = true
        };

        await using var scope =
            _factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        try
        {
            using var client = _factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing
                    .WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost")
                });

            // Act 1: Request Store A
            using var responseA1 = await client.GetAsync(
                $"/api/stores/{storeA.Slug}/customers/scope");

            Assert.Equal(HttpStatusCode.OK, responseA1.StatusCode);

            var resultA1 = await responseA1.Content
                .ReadFromJsonAsync<ScopeResponse>();

            // Act 2: Request Store B using the same HttpClient
            using var responseB = await client.GetAsync(
                $"/api/stores/{storeB.Slug}/customers/scope");

            Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

            var resultB = await responseB.Content
                .ReadFromJsonAsync<ScopeResponse>();

            // Act 3: Request Store A again
            using var responseA2 = await client.GetAsync(
                $"/api/stores/{storeA.Slug}/customers/scope");

            Assert.Equal(HttpStatusCode.OK, responseA2.StatusCode);

            var resultA2 = await responseA2.Content
                .ReadFromJsonAsync<ScopeResponse>();

            // Assert
            Assert.NotNull(resultA1);
            Assert.NotNull(resultB);
            Assert.NotNull(resultA2);

            Assert.Equal(storeA.Id, resultA1.StoreId);
            Assert.Equal(storeB.Id, resultB.StoreId);
            Assert.Equal(storeA.Id, resultA2.StoreId);

            Assert.Equal("StoreCustomer", resultA1.AccountKind);
            Assert.Equal("StoreCustomer", resultB.AccountKind);
            Assert.Equal("StoreCustomer", resultA2.AccountKind);
        }
        finally
        {
            db.Stores.RemoveRange(storeA, storeB);
            await db.SaveChangesAsync();
        }
    }

    private sealed record ScopeResponse(
        Guid StoreId,
        string AccountKind);
}
