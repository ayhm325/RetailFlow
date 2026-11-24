using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.SessionTransportSpike.Authentication;

namespace RetailFlow.SessionTransportSpike.UnitTests.Authentication;

public sealed class StoreCustomerCookieManagerTests
{
    private const string BaseCookieName =
        "__Secure-RetailFlow.StoreCustomer";

    private readonly StoreCustomerCookieManager _manager = new();

    [Fact]
    public void GetRequestCookie_UsesCookieForCurrentStoreOnly()
    {
        // Arrange
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();

        using var requestA =
            TestHttpContext.ForStoreCustomer(storeA, "store-a");

        using var requestB =
            TestHttpContext.ForStoreCustomer(storeB, "store-b");

        var cookieA = BuildCookieName(storeA);

        requestA.Context.Request.Headers["Cookie"] =
            $"{cookieA}=ticket-a";

        // Act
        var resultA = _manager.GetRequestCookie(
            requestA.Context,
            BaseCookieName);

        // Send Store A cookie to a Store B request.
        requestB.Context.Request.Headers["Cookie"] =
            $"{cookieA}=ticket-a";

        var resultB = _manager.GetRequestCookie(
            requestB.Context,
            BaseCookieName);

        // Assert
        Assert.Equal("ticket-a", resultA);
        Assert.Null(resultB);
    }

    [Fact]
    public void AppendResponseCookie_UsesStoreSpecificNameAndPath()
    {
        // Arrange
        var storeId = Guid.NewGuid();

        using var testContext =
            TestHttpContext.ForStoreCustomer(
                storeId,
                "store-a",
                "/retail");

        var cookieOptions = CreateCookieOptions();

        // Act
        _manager.AppendResponseCookie(
            testContext.Context,
            BaseCookieName,
            "ticket-a",
            cookieOptions);

        var responseCookie =
            testContext.Context.Response.Headers["Set-Cookie"]
                .ToString();

        // Assert
        var expectedName = BuildCookieName(storeId);

        Assert.Contains(
            $"{expectedName}=ticket-a",
            responseCookie);

        Assert.Contains(
            "Path=/retail/api/stores/store-a/customers",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "Secure",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "HttpOnly",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "SameSite=Lax",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Domain=",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        // The original options must not be mutated.
        Assert.Equal("/", cookieOptions.Path);
    }

    [Fact]
    public void DeleteCookie_UsesSameNameAndPathAsIssuedCookie()
    {
        // Arrange
        var storeId = Guid.NewGuid();

        using var testContext =
            TestHttpContext.ForStoreCustomer(
                storeId,
                "store-a");

        var expectedName = BuildCookieName(storeId);

        testContext.Context.Request.Headers["Cookie"] =
            $"{expectedName}=ticket-a";

        var cookieOptions = CreateCookieOptions();

        // Act
        _manager.DeleteCookie(
            testContext.Context,
            BaseCookieName,
            cookieOptions);

        var responseCookie =
            testContext.Context.Response.Headers["Set-Cookie"]
                .ToString();

        // Assert
        Assert.Contains(
            $"{expectedName}=",
            responseCookie);

        Assert.Contains(
            "Path=/api/stores/store-a/customers",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "expires=",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Domain=",
            responseCookie,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppendResponseCookie_PreservesChunkingSupport()
    {
        // Arrange
        var storeId = Guid.NewGuid();

        using var testContext =
            TestHttpContext.ForStoreCustomer(
                storeId,
                "store-a");

        var largeTicket = new string('x', 5000);

        // Act
        _manager.AppendResponseCookie(
            testContext.Context,
            BaseCookieName,
            largeTicket,
            CreateCookieOptions());

        var responseHeaders =
            testContext.Context.Response.Headers["Set-Cookie"]
                .ToArray();

        var expectedName = BuildCookieName(storeId);

        // Assert
        Assert.Contains(
            responseHeaders,
            header => header is not null &&
                header.StartsWith(
                    $"{expectedName}=chunks-",
                    StringComparison.Ordinal));

        Assert.Contains(
            responseHeaders,
            header => header is not null &&
                header.StartsWith(
                    $"{expectedName}C1=",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void GetRequestCookie_ReturnsNullWhenScopeIsMissing()
    {
        // Arrange
        using var testContext =
            TestHttpContext.WithoutIdentityScope();

        // Act
        var result = _manager.GetRequestCookie(
            testContext.Context,
            BaseCookieName);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void AppendResponseCookie_ThrowsWhenScopeIsMissing()
    {
        // Arrange
        using var testContext =
            TestHttpContext.WithoutIdentityScope();

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() =>
            _manager.AppendResponseCookie(
                testContext.Context,
                BaseCookieName,
                "invalid-ticket",
                CreateCookieOptions()));
    }

    [Fact]
    public void GetRequestCookie_DoesNotReadStoreCustomerCookieForBackOffice()
    {
        // Arrange
        using var testContext =
            TestHttpContext.ForBackOffice();

        // Act
        var result = _manager.GetRequestCookie(
            testContext.Context,
            BaseCookieName);

        // Assert
        Assert.Null(result);
    }

    private static string BuildCookieName(Guid storeId)
    {
        return $"{BaseCookieName}.store.{storeId:N}";
    }

    private static CookieOptions CreateCookieOptions()
    {
        return new CookieOptions
        {
            Path = "/",
            Secure = true,
            HttpOnly = true,
            SameSite = SameSiteMode.Lax
        };
    }

    private sealed class TestHttpContext : IDisposable
    {
        private readonly ServiceProvider _rootProvider;
        private readonly IServiceScope _requestScope;

        public DefaultHttpContext Context { get; }

        private TestHttpContext(
            ServiceProvider rootProvider,
            IServiceScope requestScope,
            DefaultHttpContext context)
        {
            _rootProvider = rootProvider;
            _requestScope = requestScope;
            Context = context;
        }

        public static TestHttpContext ForStoreCustomer(
            Guid storeId,
            string slug,
            string pathBase = "")
        {
            return Create(
                IdentityLookupScope.ForStoreCustomer(storeId),
                AccountKind.StoreCustomer,
                slug,
                pathBase);
        }

        public static TestHttpContext ForBackOffice()
        {
            return Create(
                IdentityLookupScope.ForBackOffice(),
                AccountKind.BackOffice,
                null,
                "");
        }

        public static TestHttpContext WithoutIdentityScope()
        {
            return Create(
                null,
                AccountKind.StoreCustomer,
                "store-a",
                "");
        }

        private static TestHttpContext Create(
            IdentityLookupScope? identityScope,
            AccountKind accountKind,
            string? slug,
            string pathBase)
        {
            var services = new ServiceCollection();

            services.AddScoped<
                IIdentityLookupScopeAccessor,
                IdentityLookupScopeAccessor>();

            var rootProvider = services.BuildServiceProvider();

            var requestScope = rootProvider.CreateScope();

            if (identityScope is not null)
            {
                requestScope.ServiceProvider
                    .GetRequiredService<IIdentityLookupScopeAccessor>()
                    .SetCurrent(identityScope);
            }

            var context = new DefaultHttpContext
            {
                RequestServices = requestScope.ServiceProvider
            };

            context.Request.PathBase =
                new PathString(pathBase);

            if (slug is not null)
            {
                context.Request.RouteValues["slug"] = slug;
            }

            context.SetEndpoint(
                new Endpoint(
                    static _ => Task.CompletedTask,
                    new EndpointMetadataCollection(
                        new IdentityScopeMetadata(accountKind)),
                    "Test endpoint"));

            return new TestHttpContext(
                rootProvider,
                requestScope,
                context);
        }

        public void Dispose()
        {
            _requestScope.Dispose();
            _rootProvider.Dispose();
        }
    }
}
