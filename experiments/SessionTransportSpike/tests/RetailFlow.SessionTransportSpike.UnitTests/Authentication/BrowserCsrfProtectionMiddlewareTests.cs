using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RetailFlow.SessionTransportSpike.Authentication;

namespace RetailFlow.SessionTransportSpike.UnitTests.Authentication;

public sealed class BrowserCsrfProtectionMiddlewareTests
{
    private const string TrustedOrigin =
        "https://retailflow.example.com";

    [Theory]
    [InlineData("https://retailflow.example.com", true)]
    [InlineData("https://localhost", true)]
    [InlineData("https://localhost:5001", true)]
    [InlineData("http://localhost:5173", true)]
    [InlineData("http://127.0.0.1:5173", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("retailflow.example.com", false)]
    [InlineData("ftp://retailflow.example.com", false)]
    [InlineData("http://retailflow.example.com", false)]
    [InlineData("https://retailflow.example.com/", false)]
    [InlineData("https://retailflow.example.com/app", false)]
    [InlineData("https://retailflow.example.com?x=1", false)]
    [InlineData("https://retailflow.example.com#frag", false)]
    [InlineData("https://user@retailflow.example.com", false)]
    public void IsValidBrowserOrigin_ReturnsExpectedResult(
        string rawOrigin,
        bool expected)
    {
        var actual =
            BrowserCsrfProtectionOptions.IsValidBrowserOrigin(rawOrigin);

        Assert.Equal(expected, actual);

        var validator = new BrowserCsrfProtectionOptionsValidator(
            new TestHostEnvironment("Development"));
        var result = validator.Validate(
            Options.DefaultName,
            new BrowserCsrfProtectionOptions
            {
                BrowserOrigin = rawOrigin
            });

        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task InvokeAsync_WhenRequestIsValid_AllowsRequest()
    {
        var result = await ExecuteAsync();

        Assert.True(result.NextCalled);
        Assert.Equal(
            StatusCodes.Status200OK,
            result.Context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenCustomHeaderIsMissing_ReturnsForbidden()
    {
        var result = await ExecuteAsync(includeRequestHeader: false);
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenCustomHeaderValueIsInvalid_ReturnsForbidden()
    {
        var result = await ExecuteAsync(requestHeaderValue: "anything-else");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginIsExternal_ReturnsForbidden()
    {
        var result = await ExecuteAsync(origin: "https://attacker.example");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginIsNull_ReturnsForbidden()
    {
        var result = await ExecuteAsync(origin: "null");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginContainsTrailingSlash_ReturnsForbidden()
    {
        var result = await ExecuteAsync(origin: $"{TrustedOrigin}/");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginContainsPath_ReturnsForbidden()
    {
        var result = await ExecuteAsync(origin: $"{TrustedOrigin}/attacker");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginUsesAttackerSuffix_ReturnsForbidden()
    {
        var result = await ExecuteAsync(
            origin: "https://retailflow.example.com.attacker.test");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginUsesDifferentPort_ReturnsForbidden()
    {
        var result = await ExecuteAsync(
            origin: "https://retailflow.example.com:444");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginIsMissingAndRefererIsTrusted_AllowsRequest()
    {
        var result = await ExecuteAsync(
            includeOrigin: false,
            includeReferer: true,
            referer: $"{TrustedOrigin}/store-a");

        Assert.True(result.NextCalled);
        Assert.Equal(
            StatusCodes.Status200OK,
            result.Context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginIsInvalidDoesNotFallbackToReferer()
    {
        var result = await ExecuteAsync(
            origin: "https://attacker.example",
            includeReferer: true,
            referer: $"{TrustedOrigin}/store-a");

        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenOriginAndRefererAreMissing_ReturnsForbidden()
    {
        var result = await ExecuteAsync(
            includeOrigin: false,
            includeReferer: false);

        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenRefererIsExternal_ReturnsForbidden()
    {
        var result = await ExecuteAsync(
            includeOrigin: false,
            includeReferer: true,
            referer: "https://attacker.example/page");

        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenFetchSiteIsCrossSite_ReturnsForbidden()
    {
        var result = await ExecuteAsync(fetchSite: "cross-site");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenFetchSiteIsSameSite_ReturnsForbidden()
    {
        var result = await ExecuteAsync(fetchSite: "same-site");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenFetchSiteIsNone_ReturnsForbidden()
    {
        var result = await ExecuteAsync(fetchSite: "none");
        AssertForbidden(result);
    }

    [Fact]
    public async Task InvokeAsync_WhenFetchMetadataIsMissing_UsesOriginValidation()
    {
        var result = await ExecuteAsync(includeFetchSite: false);

        Assert.True(result.NextCalled);
        Assert.Equal(
            StatusCodes.Status200OK,
            result.Context.Response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    [InlineData("TRACE")]
    public async Task InvokeAsync_WhenMethodIsSafe_SkipsCsrfValidation(
        string method)
    {
        var result = await ExecuteAsync(
            method: method,
            includeRequestHeader: false,
            includeOrigin: false,
            includeReferer: false,
            includeFetchSite: false);

        Assert.True(result.NextCalled);
        Assert.Equal(
            StatusCodes.Status200OK,
            result.Context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_WhenMethodIsUnknown_RequiresCsrfValidation()
    {
        var result = await ExecuteAsync(
            method: "CUSTOM",
            includeRequestHeader: false);

        AssertForbidden(result);
    }

    private static async Task<InvocationResult> ExecuteAsync(
        string method = "POST",
        string? requestHeaderValue = "1",
        bool includeRequestHeader = true,
        string? origin = TrustedOrigin,
        bool includeOrigin = true,
        string? referer = null,
        bool includeReferer = false,
        string? fetchSite = "same-origin",
        bool includeFetchSite = true)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.RequestServices = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        context.Response.Body = new MemoryStream();

        if (includeRequestHeader)
        {
            context.Request.Headers[
                BrowserCsrfProtectionOptions.RequestHeaderName] =
                requestHeaderValue;
        }

        if (includeOrigin)
        {
            context.Request.Headers["Origin"] = origin;
        }

        if (includeReferer)
        {
            context.Request.Headers["Referer"] = referer;
        }

        if (includeFetchSite)
        {
            context.Request.Headers["Sec-Fetch-Site"] = fetchSite;
        }

        var nextCalled = false;

        var options = Options.Create(
            new BrowserCsrfProtectionOptions
            {
                BrowserOrigin = TrustedOrigin
            });

        var middleware = new BrowserCsrfProtectionMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            options,
            NullLogger<BrowserCsrfProtectionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        return new InvocationResult(context, nextCalled);
    }

    private static void AssertForbidden(InvocationResult result)
    {
        Assert.False(result.NextCalled);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            result.Context.Response.StatusCode);

        Assert.StartsWith(
            "application/problem+json",
            result.Context.Response.ContentType);

        Assert.Equal(
            "no-store",
            result.Context.Response.Headers["Cache-Control"].ToString());
    }

    private sealed record InvocationResult(
        DefaultHttpContext Context,
        bool NextCalled);
}
