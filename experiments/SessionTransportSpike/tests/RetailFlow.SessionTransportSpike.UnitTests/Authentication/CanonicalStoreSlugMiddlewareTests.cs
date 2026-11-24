using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.SessionTransportSpike.Authentication;

namespace RetailFlow.SessionTransportSpike.UnitTests.Authentication;

public sealed class CanonicalStoreSlugMiddlewareTests
{
    [Theory]
    [InlineData(
        "Store-A",
        "/api/stores/Store-A/customers/login")]
    [InlineData(
        "store-a",
        "/API/stores/store-a/customers/login")]
    [InlineData(
        "store-a",
        "/api/stores/store-a/CUSTOMERS/login")]
    public async Task InvokeAsync_WhenStorePathIsNotCanonical_ReturnsBadRequest(
        string slug,
        string path)
    {
        // Arrange
        var context = CreateContext(
            AccountKind.StoreCustomer,
            slug,
            path);

        context.Request.Method = HttpMethods.Post;

        var nextCalled = false;

        var middleware = new CanonicalStoreSlugMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(
            StatusCodes.Status400BadRequest,
            context.Response.StatusCode);

        Assert.Equal(
            "application/problem+json",
            context.Response.ContentType);

        Assert.False(nextCalled);

        Assert.Empty(
            context.Response.Headers["Location"].ToString());

        Assert.Equal(
            HttpMethods.Post,
            context.Request.Method);
    }

    [Fact]
    public async Task InvokeAsync_WhenPathIsCanonical_ContinuesPipeline()
    {
        // Arrange
        var context = CreateContext(
            AccountKind.StoreCustomer,
            "store-a",
            "/api/stores/store-a/customers/me");

        var nextCalled = false;

        var middleware = new CanonicalStoreSlugMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);

        Assert.Equal(
            StatusCodes.Status200OK,
            context.Response.StatusCode);

        Assert.Empty(context.Response.Headers["Location"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_WhenEndpointIsBackOffice_DoesNotRedirect()
    {
        // Arrange
        var context = CreateContext(
            AccountKind.BackOffice,
            "Store-A",
            "/api/backoffice/me");

        var nextCalled = false;

        var middleware = new CanonicalStoreSlugMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextCalled);

        Assert.Equal(
            StatusCodes.Status200OK,
            context.Response.StatusCode);

        Assert.Empty(context.Response.Headers["Location"].ToString());
    }

    private static DefaultHttpContext CreateContext(
        AccountKind accountKind,
        string slug,
        string path)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider()
        };

        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new IdentityScopeMetadata(accountKind)),
            "Test endpoint");

        context.SetEndpoint(endpoint);

        context.Request.RouteValues["slug"] = slug;
        context.Request.Path = new PathString(path);

        return context;
    }
}
