using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Infrastructure composition and HTTP pipeline extensions for the RetailFlow Product Host.
/// </summary>
public static class ProductHostExtensions
{
    public static IServiceCollection AddProductHostInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddProblemDetails();
        services.AddOpenApi();
        services.AddHealthChecks();

        services.AddSingleton<IValidateOptions<HostFilteringOptions>>(
            _ => new HostFilteringAllowedHostsValidator(environment));

        services
            .AddOptions<HostFilteringOptions>()
            .ValidateOnStart();

        return services;
    }

    public static WebApplication UseProductHostPipeline(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // 1. Forwarded Headers must execute before HSTS and HTTPS Redirection.
        //    In Azure App Service on Linux, ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
        //    enables X-Forwarded-For and X-Forwarded-Proto (note: it clears KnownProxies/KnownNetworks,
        //    so Kestrel must remain isolated behind App Service and live behavior must be verified).
        app.UseForwardedHeaders();

        // 2. Centralized exception handling outside Development (returns RFC 9457 ProblemDetails
        //    without exposing stack traces or internal details).
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler();

            // 3. HSTS outside Development.
            app.UseHsts();
        }

        // 4. HTTPS Redirection.
        app.UseHttpsRedirection();

        // 5. Default Files & 6. Static Files for future React SPA assets in wwwroot.
        app.UseDefaultFiles();
        app.UseStaticFiles();

        return app;
    }

    public static WebApplication MapProductHostEndpoints(
        this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // 7. Development-only OpenAPI endpoint.
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        // 8. Health endpoints (unauthenticated; readiness does not yet check a DB
        //    because no production DbContext exists in this bootstrap stage).
        app.MapHealthChecks(
            "/health/live",
            new HealthCheckOptions
            {
                Predicate = _ => false,
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

        app.MapHealthChecks(
            "/health/ready",
            new HealthCheckOptions
            {
                Predicate = registration => registration.Tags.Contains("ready"),
                ResultStatusCodes =
                {
                    [HealthStatus.Healthy] = StatusCodes.Status200OK,
                    [HealthStatus.Degraded] = StatusCodes.Status200OK,
                    [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                }
            });

        // 9. Conditional React SPA fallback (only when wwwroot/index.html exists,
        //    never intercepts /api/* or /health/*, and only handles browser navigation requests).
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;

            if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (!HttpMethods.IsGet(context.Request.Method) &&
                !HttpMethods.IsHead(context.Request.Method))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            if (Path.HasExtension(path.Value))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var acceptHeader = context.Request.Headers.Accept.ToString();
            var acceptsHtml =
                !string.IsNullOrWhiteSpace(acceptHeader) &&
                acceptHeader.Contains("text/html", StringComparison.OrdinalIgnoreCase);

            if (!acceptsHtml)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var indexFile = app.Environment.WebRootFileProvider.GetFileInfo("index.html");

            if (!indexFile.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";

            if (HttpMethods.IsHead(context.Request.Method))
            {
                return;
            }

            await using var stream = indexFile.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        });

        return app;
    }

    private sealed class HostFilteringAllowedHostsValidator
        : IValidateOptions<HostFilteringOptions>
    {
        private readonly IHostEnvironment _environment;

        public HostFilteringAllowedHostsValidator(IHostEnvironment environment)
        {
            _environment = environment;
        }

        public ValidateOptionsResult Validate(
            string? name,
            HostFilteringOptions options)
        {
            if (_environment.IsDevelopment())
            {
                return ValidateOptionsResult.Success;
            }

            if (options.AllowedHosts is null || options.AllowedHosts.Count == 0)
            {
                return ValidateOptionsResult.Fail(
                    "AllowedHosts must be explicitly configured outside Development.");
            }

            foreach (var host in options.AllowedHosts)
            {
                if (string.IsNullOrWhiteSpace(host) ||
                    host.StartsWith('.') ||
                    host.Contains('*', StringComparison.Ordinal) ||
                    host.Contains('/', StringComparison.Ordinal) ||
                    host.Any(char.IsWhiteSpace))
                {
                    return ValidateOptionsResult.Fail(
                        $"AllowedHosts contains an invalid or unrestricted value '{host}' outside Development.");
                }
            }

            return ValidateOptionsResult.Success;
        }
    }
}
