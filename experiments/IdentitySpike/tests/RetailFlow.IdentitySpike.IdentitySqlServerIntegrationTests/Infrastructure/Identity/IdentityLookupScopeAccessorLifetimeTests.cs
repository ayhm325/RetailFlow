using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentityLookupScopeAccessorLifetimeTests
{
    [Fact]
    public async Task EachHttpRequest_ReceivesIndependentAccessorInstance()
    {
        // Arrange: configure test-only instrumentation.
        var tracker = new AccessorInstanceTracker();

        using var factory = new IdentitySpikeApiFactory(services =>
        {
            services.AddSingleton(tracker);

            services.RemoveAll<IIdentityLookupScopeAccessor>();

            services.AddScoped<IIdentityLookupScopeAccessor>(provider =>
            {
                var accessor = new IdentityLookupScopeAccessor();

                provider
                    .GetRequiredService<AccessorInstanceTracker>()
                    .Record(accessor);

                return accessor;
            });
        });

        var storeA = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = "Accessor Lifetime Store A",
            Slug = $"accessor-a-{Guid.NewGuid():N}",
            IsActive = true
        };

        var storeB = new SpikeStore
        {
            Id = Guid.NewGuid(),
            Name = "Accessor Lifetime Store B",
            Slug = $"accessor-b-{Guid.NewGuid():N}",
            IsActive = true
        };

        await using var seedScope =
            factory.Services.CreateAsyncScope();

        var db = seedScope.ServiceProvider
            .GetRequiredService<IdentitySpikeDbContext>();

        db.Stores.AddRange(storeA, storeB);
        await db.SaveChangesAsync();

        try
        {
            using var client = factory.CreateClient(
                new WebApplicationFactoryClientOptions
                {
                    BaseAddress = new Uri("https://localhost")
                });

            // Act: three sequential requests through the same client.

            using var responseA1 = await client.GetAsync(
                $"/api/stores/{storeA.Slug}/customers/scope");

            Assert.Equal(HttpStatusCode.OK, responseA1.StatusCode);

            using var responseB = await client.GetAsync(
                $"/api/stores/{storeB.Slug}/customers/scope");

            Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);

            using var responseA2 = await client.GetAsync(
                $"/api/stores/{storeA.Slug}/customers/scope");

            Assert.Equal(HttpStatusCode.OK, responseA2.StatusCode);

            // Assert: each request created an accessor instance.
            var instances = tracker.Instances;

            Assert.Equal(3, instances.Count);

            // Assert: actual object references are different.
            Assert.NotSame(instances[0], instances[1]);
            Assert.NotSame(instances[1], instances[2]);
            Assert.NotSame(instances[0], instances[2]);

            // Assert: each instance retained its own request scope.
            Assert.Equal(storeA.Id, instances[0].Current?.StoreId);
            Assert.Equal(storeB.Id, instances[1].Current?.StoreId);
            Assert.Equal(storeA.Id, instances[2].Current?.StoreId);
        }
        finally
        {
            db.Stores.RemoveRange(storeA, storeB);
            await db.SaveChangesAsync();
        }
    }
}
