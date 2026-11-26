using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using RetailFlow.Api.Auth;
using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

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

        services.AddDataProtection()
            .SetApplicationName("RetailFlow")
            .PersistKeysToDbContext<RetailFlowDbContext>();

        services.AddSingleton<ITicketStore, SqlSessionTicketStore>();
        services.AddScoped<RetailFlowCookieEvents>();
        services.AddHostedService<MaintenanceBackgroundService>();
        services.AddHostedService<PlatformBootstrapHostedService>();
        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-RetailFlow.Session";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.Path = "/";
                options.SlidingExpiration = true;
                options.ExpireTimeSpan = TimeSpan.FromHours(12);
                options.EventsType = typeof(RetailFlowCookieEvents);
            });

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<ITicketStore>((options, ticketStore) => options.SessionStore = ticketStore);

        services.AddAuthorizationBuilder()
            .AddPolicy("Customer", policy => policy.RequireRole(nameof(AppRole.Customer)))
            .AddPolicy("MerchantOwner", policy => policy.RequireRole(nameof(AppRole.MerchantOwner)))
            .AddPolicy("PlatformAdmin", policy => policy.RequireRole(nameof(AppRole.PlatformAdmin)));

        services.Configure<RateLimitingOptions>(configuration.GetSection("RateLimiting"));
        services.AddRateLimiter();
        services.AddOptions<RateLimiterOptions>()
            .Configure<IOptions<RateLimitingOptions>>((options, rateLimitOptions) =>
            {
                var settings = rateLimitOptions.Value;
                options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
                options.OnRejected = async (context, cancellationToken) =>
                {
                    context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    {
                        context.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                    }

                    var problemDetailsService = context.HttpContext.RequestServices.GetService<IProblemDetailsService>();
                    if (problemDetailsService is not null)
                    {
                        await problemDetailsService.WriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = context.HttpContext,
                            ProblemDetails = new ProblemDetails
                            {
                                Status = StatusCodes.Status429TooManyRequests,
                                Title = "Too Many Requests",
                                Detail = "Rate limit exceeded. Please retry later."
                            }
                        });
                    }
                };

                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                {
                    if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
                    {
                        return RateLimitPartition.GetNoLimiter("health");
                    }

                    var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var partitionKey = BuildRateLimitPartitionKey(httpContext.Request.Path, ip);
                    return RateLimitPartition.GetFixedWindowLimiter(
                        partitionKey,
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = Math.Max(1, settings.PermitLimit),
                            Window = TimeSpan.FromSeconds(Math.Max(1, settings.WindowSeconds)),
                            QueueLimit = 0,
                            AutoReplenishment = true
                        });
                });
            });

        services.AddHealthChecks()
            .AddCheck<DatabaseReadinessHealthCheck>(
                "database",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["ready"]);

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

        // 5. Security Response Headers (OWASP A05 / ASVS V14.4).
        app.Use(async (context, next) =>
        {
            context.Response.Headers.TryAdd("X-Content-Type-Options", "nosniff");
            context.Response.Headers.TryAdd("X-Frame-Options", "DENY");
            context.Response.Headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            await next();
        });

        // 6. Rate Limiting (OWASP API4:2023).
        app.UseRateLimiter();

        // 7. Authentication, CSRF Protection (OWASP REF-06), & Authorization (AUTH-ADR-01).
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            var isUnsafeMethod =
                !HttpMethods.IsGet(context.Request.Method) &&
                !HttpMethods.IsHead(context.Request.Method) &&
                !HttpMethods.IsOptions(context.Request.Method);

            if (isUnsafeMethod && context.User.Identity?.IsAuthenticated == true)
            {
                var hasCsrfMarker = string.Equals(
                    context.Request.Headers["X-CSRF"].FirstOrDefault(),
                    "1",
                    StringComparison.Ordinal);

                var originValid = IsSameOriginIfPresent(context.Request);

                if (!hasCsrfMarker || !originValid)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    var problemDetailsService = context.RequestServices.GetService<IProblemDetailsService>();
                    if (problemDetailsService is not null)
                    {
                        await problemDetailsService.WriteAsync(new ProblemDetailsContext
                        {
                            HttpContext = context,
                            ProblemDetails = new ProblemDetails
                            {
                                Status = StatusCodes.Status403Forbidden,
                                Title = "CSRF Validation Failed",
                                Detail = !hasCsrfMarker
                                    ? "Missing or invalid X-CSRF header for authenticated state-changing request."
                                    : "Origin/Referer header does not match request host."
                            }
                        });
                    }

                    return;
                }
            }

            await next();
        });
        app.UseAuthorization();

        // 8. Default Files & 9. Static Files for future React SPA assets in wwwroot.
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

        // 8. Health endpoints (unauthenticated; /health/live checks process liveness only,
        //    while /health/ready checks dependencies tagged "ready" including SQL Server).
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

            // SPA fallback negotiation depends on the Accept header.
            context.Response.Headers.Append("Vary", "Accept");

            if (!MediaTypeHeaderValue.TryParseList(
                    context.Request.Headers.Accept,
                    out var acceptHeaders) ||
                acceptHeaders.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var textHtmlEntries = acceptHeaders
                .Where(mediaType => mediaType.MediaType.Equals(
                    "text/html",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var acceptsHtml =
                textHtmlEntries.Length > 0 &&
                textHtmlEntries.All(IsAcceptableHtmlMediaType);

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

    private static bool IsAcceptableHtmlMediaType(MediaTypeHeaderValue mediaType)
    {
        var hasQualityParameter = mediaType.Parameters.Any(parameter =>
            parameter.Name.Equals("q", StringComparison.OrdinalIgnoreCase));

        if (hasQualityParameter && !mediaType.Quality.HasValue)
        {
            return false;
        }

        var quality = mediaType.Quality ?? 1.0d;
        return quality > 0d && quality <= 1.0d;
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
                    host.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                    host.Equals("[::]", StringComparison.OrdinalIgnoreCase) ||
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

    private static bool IsSameOriginIfPresent(HttpRequest request)
    {
        var secFetchSite = request.Headers["Sec-Fetch-Site"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(secFetchSite) &&
            !string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var headerValue = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            headerValue = request.Headers.Referer.FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return true;
        }

        if (!Uri.TryCreate(headerValue, UriKind.Absolute, out var sourceUri))
        {
            return false;
        }

        var expectedPort = request.Host.Port ?? (string.Equals(request.Scheme, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        return string.Equals(sourceUri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(sourceUri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase) &&
               sourceUri.Port == expectedPort;
    }

    private static string BuildRateLimitPartitionKey(PathString path, string ip)
    {
        if (path.StartsWithSegments("/api/stores", StringComparison.OrdinalIgnoreCase))
        {
            var segments = path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments is { Length: >= 3 })
            {
                var normalizedSlug = segments[2].Trim().ToLowerInvariant();
                return $"store:{normalizedSlug}:ip:{ip}";
            }
        }

        return $"global:ip:{ip}";
    }

    private sealed class RateLimitingOptions
    {
        public int PermitLimit { get; set; } = 120;
        public int WindowSeconds { get; set; } = 60;
    }
}
