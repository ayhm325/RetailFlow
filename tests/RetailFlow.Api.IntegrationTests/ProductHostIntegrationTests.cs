using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace RetailFlow.Api.IntegrationTests;

public sealed class ProductHostIntegrationTests
{
    private const string ProductionHost = "retail.example.com";

    [Fact]
    public async Task DevelopmentHost_StartsAndServesLiveReadyAndOpenApi()
    {
        await using var factory = CreateDevelopmentFactory();
        using var client = CreateHttpsClient(factory, "localhost");

        using var liveResponse = await client.GetAsync("/health/live");
        var liveBody = await liveResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);
        Assert.Equal("Healthy", liveBody);
        AssertNoInternalLeakage(liveBody);

        using var readyResponse = await client.GetAsync("/health/ready");
        var readyBody = await readyResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, readyResponse.StatusCode);
        Assert.Equal("Healthy", readyBody);
        AssertNoInternalLeakage(readyBody);

        using var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var openApiBody = await openApiResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Contains("\"openapi\"", openApiBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionHost_WithMissingAllowedHosts_FailsStartup()
    {
        using var factory = CreateProductionFactory(allowedHosts: null);

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionHost_WithWildcardAllowedHosts_FailsStartup()
    {
        using var factory = CreateProductionFactory(allowedHosts: "*");

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("retail.example.com;0.0.0.0")]
    [InlineData("retail.example.com;[::]")]
    [InlineData("   ")]
    [InlineData(".example.com")]
    public void ProductionHost_WithUnsafeOrEmptyAllowedHosts_FailsStartup(string allowedHosts)
    {
        using var factory = CreateProductionFactory(allowedHosts);

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("staging.example.com;0.0.0.0")]
    [InlineData("staging.example.com;[::]")]
    public void StagingHost_WithWildcardOrUnsafeAllowedHosts_FailsStartup(string allowedHosts)
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Microsoft.Extensions.Hosting.Environments.Staging);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = allowedHosts
                    });
                });
            });

        var exception = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("AllowedHosts", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProductionHost_AcceptsConfiguredHost_RejectsDisallowedHost_AndDisablesOpenApi()
    {
        await using var factory = CreateProductionFactory(ProductionHost);

        using var allowedClient = CreateHttpsClient(factory, ProductionHost);
        using var liveResponse = await allowedClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        using var openApiResponse = await allowedClient.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.NotFound, openApiResponse.StatusCode);

        using var disallowedClient = CreateHttpsClient(factory, "evil.example.com");
        using var rejectedResponse = await disallowedClient.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);
    }

    [Fact]
    public async Task ProductionHost_WhenForwardedHeadersEnabled_AcceptsXForwardedProtoHttps()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Microsoft.Extensions.Hosting.Environments.Production);
                builder.UseSetting("FORWARDEDHEADERS_ENABLED", "true");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = ProductionHost
                    });
                });
            });

        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri($"http://{ProductionHost}"),
                AllowAutoRedirect = false
            });

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Forwarded-Proto", "https");

        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ProductionHost_ReturnsProblemDetailsWithoutStackTrace_OnUnhandledException()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Microsoft.Extensions.Hosting.Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = ProductionHost
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IStartupFilter>(
                        new ThrowingWebRootStartupFilter());
                });
            });

        using var client = CreateHttpsClient(factory, ProductionHost);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/store-a/catalog");
        request.Headers.Add("Accept", "application/json, text/html");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("Sensitive internal failure", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ThrowingFileProvider", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StaticFilesAndSpaFallback_WorkWhenIndexExists_AndKeepApiRoutesAs404()
    {
        var webRoot = CreateTempWebRoot(withIndexHtml: true, withStaticAsset: true);

        try
        {
            await using var factory = CreateDevelopmentFactory(webRoot);
            using var client = CreateHttpsClient(factory, "localhost");

            // 1. Existing static asset is served.
            using var assetResponse = await client.GetAsync("/app.js");
            var assetBody = await assetResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, assetResponse.StatusCode);
            Assert.Contains("retailflow-static-asset", assetBody, StringComparison.Ordinal);

            // 2. SPA fallback serves index.html for client navigation routes and sets Vary: Accept.
            using var spaRequest = new HttpRequestMessage(HttpMethod.Get, "/store-a/catalog");
            spaRequest.Headers.Add("Accept", "text/html");

            using var spaResponse = await client.SendAsync(spaRequest);
            var spaBody = await spaResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, spaResponse.StatusCode);
            Assert.Equal("text/html", spaResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Accept", spaResponse.Headers.Vary, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("retailflow-test-spa", spaBody, StringComparison.Ordinal);

            // 2b. HEAD request for SPA route succeeds with text/html and empty body.
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, "/store-a/catalog");
            headRequest.Headers.Add("Accept", "text/html");

            using var headResponse = await client.SendAsync(headRequest);
            var headBody = await headResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, headResponse.StatusCode);
            Assert.Equal("text/html", headResponse.Content.Headers.ContentType?.MediaType);
            Assert.Contains("Accept", headResponse.Headers.Vary, StringComparer.OrdinalIgnoreCase);
            Assert.Empty(headBody);

            // 3. Unknown /api route must return 404, never index.html.
            using var apiRequest = new HttpRequestMessage(HttpMethod.Get, "/api/not-existing");
            apiRequest.Headers.Add("Accept", "text/html");

            using var apiResponse = await client.SendAsync(apiRequest);
            var apiBody = await apiResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.NotFound, apiResponse.StatusCode);
            Assert.DoesNotContain("retailflow-test-spa", apiBody, StringComparison.Ordinal);

            // 4. Unknown /health route must return 404, never index.html.
            using var healthResponse = await client.GetAsync("/health/not-existing");
            Assert.Equal(HttpStatusCode.NotFound, healthResponse.StatusCode);

            // 5. Missing static asset must return 404, never index.html.
            using var missingAssetRequest = new HttpRequestMessage(HttpMethod.Get, "/assets/missing.js");
            missingAssetRequest.Headers.Add("Accept", "text/html");
            using var missingAssetResponse = await client.SendAsync(missingAssetRequest);
            Assert.Equal(HttpStatusCode.NotFound, missingAssetResponse.StatusCode);

            // 6. Non-browser requests (no Accept or Accept: application/json) must not receive index.html.
            using var noAcceptResponse = await client.GetAsync("/store-a/catalog");
            Assert.Equal(HttpStatusCode.NotFound, noAcceptResponse.StatusCode);

            using var jsonAcceptRequest = new HttpRequestMessage(HttpMethod.Get, "/store-a/catalog");
            jsonAcceptRequest.Headers.Add("Accept", "application/json");
            using var jsonAcceptResponse = await client.SendAsync(jsonAcceptRequest);
            Assert.Equal(HttpStatusCode.NotFound, jsonAcceptResponse.StatusCode);

            // 7. Non-GET/HEAD requests must not receive index.html.
            using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/store-a/catalog");
            postRequest.Headers.Add("Accept", "text/html");
            using var postResponse = await client.SendAsync(postRequest);
            Assert.Equal(HttpStatusCode.NotFound, postResponse.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(webRoot);
        }
    }

    [Theory]
    [InlineData("text/html", HttpStatusCode.OK)]
    [InlineData("text/html;q=1", HttpStatusCode.OK)]
    [InlineData("text/html;q=0.5", HttpStatusCode.OK)]
    [InlineData("text/html;q=0", HttpStatusCode.NotFound)]
    [InlineData("application/json, text/html;q=0", HttpStatusCode.NotFound)]
    [InlineData("application/json, text/html;q=0, */*;q=1", HttpStatusCode.NotFound)]
    [InlineData("text/html;q=0, text/html;q=0.8", HttpStatusCode.NotFound)]
    [InlineData("application/json", HttpStatusCode.NotFound)]
    [InlineData("*/*", HttpStatusCode.NotFound)]
    [InlineData("invalid/media/type;;;", HttpStatusCode.NotFound)]
    [InlineData("text/html;q=notanumber", HttpStatusCode.NotFound)]
    public async Task SpaFallback_RespectsTypedAcceptHeaderQualityNegotiation(
        string acceptHeader,
        HttpStatusCode expectedStatus)
    {
        var webRoot = CreateTempWebRoot(withIndexHtml: true, withStaticAsset: false);

        try
        {
            await using var factory = CreateDevelopmentFactory(webRoot);
            using var client = CreateHttpsClient(factory, "localhost");

            using var request = new HttpRequestMessage(HttpMethod.Get, "/store-a/catalog");
            request.Headers.TryAddWithoutValidation("Accept", acceptHeader);

            using var response = await client.SendAsync(request);
            Assert.Equal(expectedStatus, response.StatusCode);

            if (expectedStatus == HttpStatusCode.OK)
            {
                Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
                Assert.Contains("Accept", response.Headers.Vary, StringComparer.OrdinalIgnoreCase);
            }
        }
        finally
        {
            DeleteTempDirectory(webRoot);
        }
    }

    [Fact]
    public async Task HostStartsCleanly_WhenIndexHtmlIsAbsent()
    {
        var webRoot = CreateTempWebRoot(withIndexHtml: false, withStaticAsset: false);

        try
        {
            await using var factory = CreateDevelopmentFactory(webRoot);
            using var client = CreateHttpsClient(factory, "localhost");

            using var liveResponse = await client.GetAsync("/health/live");
            Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

            using var missingSpaResponse = await client.GetAsync("/store-a/catalog");
            Assert.Equal(HttpStatusCode.NotFound, missingSpaResponse.StatusCode);
        }
        finally
        {
            DeleteTempDirectory(webRoot);
        }
    }

    private static WebApplicationFactory<Program> CreateDevelopmentFactory(
        string? webRootPath = null)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Microsoft.Extensions.Hosting.Environments.Development);
                if (webRootPath is not null)
                {
                    builder.UseWebRoot(webRootPath);
                }
            });
    }

    private static WebApplicationFactory<Program> CreateProductionFactory(
        string? allowedHosts)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Microsoft.Extensions.Hosting.Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    if (allowedHosts is not null)
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["AllowedHosts"] = allowedHosts
                        });
                    }
                });
            });
    }

    private static HttpClient CreateHttpsClient(
        WebApplicationFactory<Program> factory,
        string host)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri($"https://{host}"),
                AllowAutoRedirect = false
            });
    }

    private static void AssertNoInternalLeakage(string body)
    {
        Assert.DoesNotContain("ConnectionStrings", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASPNETCORE_", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreateTempWebRoot(
        bool withIndexHtml,
        bool withStaticAsset)
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "RetailFlow.ProductHostTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);

        if (withIndexHtml)
        {
            File.WriteAllText(
                Path.Combine(path, "index.html"),
                "<!doctype html><html><body>retailflow-test-spa</body></html>");
        }

        if (withStaticAsset)
        {
            File.WriteAllText(
                Path.Combine(path, "app.js"),
                "console.log('retailflow-static-asset');");
        }

        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed class ThrowingWebRootStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                var env = app.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
                env.WebRootFileProvider = new ThrowingFileProvider("index.html");
                next(app);
            };
        }
    }

    private sealed class ThrowingFileProvider : IFileProvider
    {
        private readonly string _triggerPath;

        public ThrowingFileProvider(string triggerPath)
        {
            _triggerPath = triggerPath;
        }

        public IDirectoryContents GetDirectoryContents(string subpath)
            => NotFoundDirectoryContents.Singleton;

        public IFileInfo GetFileInfo(string subpath)
        {
            if (string.Equals(subpath, _triggerPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Sensitive internal failure");
            }

            return new NotFoundFileInfo(subpath);
        }

        public IChangeToken Watch(string filter)
            => NullChangeToken.Singleton;
    }
}
