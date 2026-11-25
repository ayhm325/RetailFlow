using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

using RetailFlow.SessionTransportSpike.Api.Authentication;
using RetailFlow.SessionTransportSpike.Api.Authorization;
using RetailFlow.SessionTransportSpike.Api.Contracts;
using RetailFlow.SessionTransportSpike.Api.Infrastructure.DataProtection;
using RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.Authentication;

var builder = WebApplication.CreateBuilder(args);

// -----------------------------------------------------------------------------
// Database
// -----------------------------------------------------------------------------

var connectionString =
    Environment.GetEnvironmentVariable(
        "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
    ?? builder.Configuration.GetConnectionString("IdentitySpike")
    ?? "Server=.;Database=RetailFlowIdentitySpike;" +
       "Trusted_Connection=True;Encrypt=True;" +
       "TrustServerCertificate=True;MultipleActiveResultSets=True";

builder.Services.AddDbContext<IdentitySpikeDbContext>(
    options => options.UseSqlServer(connectionString));

builder.Services.AddDbContextFactory<SessionTicketDbContext>(
    options =>
    {
        options.UseSqlServer(
            connectionString,
            sqlServer =>
            {
                sqlServer.MigrationsHistoryTable(
                    SessionTicketDbContextFactory.MigrationsHistoryTableName);
            });
    });

// -----------------------------------------------------------------------------
// Identity Scope
// -----------------------------------------------------------------------------

builder.Services.AddScoped<
    IIdentityLookupScopeAccessor,
    IdentityLookupScopeAccessor>();

builder.Services.AddScoped<
    IIdentityScopeResolver,
    IdentityScopeResolver>();

// -----------------------------------------------------------------------------
// Shared Data Protection
// -----------------------------------------------------------------------------

var dataProtectionApplicationName =
    builder.Configuration["DataProtection:ApplicationName"]
    ?? RetailFlowDataProtectionDefaults.ApplicationName;

if (string.IsNullOrWhiteSpace(dataProtectionApplicationName))
{
    throw new InvalidOperationException(
        "Data Protection ApplicationName cannot be empty.");
}

using var dataProtectionCertificate = DataProtectionCertificateLoader.Load(
    builder.Configuration);

var dataProtectionBuilder = builder.Services
    .AddDataProtection()
    .SetApplicationName(dataProtectionApplicationName);

var testingKeysDirectory = builder.Configuration["DataProtection:KeysDirectory"];

if (builder.Environment.IsEnvironment("Testing") &&
    !string.IsNullOrWhiteSpace(testingKeysDirectory))
{
    var keysDirectoryInfo = Directory.CreateDirectory(testingKeysDirectory);
    dataProtectionBuilder.PersistKeysToFileSystem(keysDirectoryInfo);
}
else
{
    dataProtectionBuilder.PersistKeysToDbContext<SessionTicketDbContext>();
}

dataProtectionBuilder
    .ProtectKeysWithCertificate(dataProtectionCertificate)
    .UnprotectKeysWithAnyCertificate(dataProtectionCertificate);

// -----------------------------------------------------------------------------
// ASP.NET Core Identity
// -----------------------------------------------------------------------------

builder.Services.AddScoped<ScopedUserStore>();

builder.Services.AddScoped<
    IStoreCustomerPasswordChangeService,
    StoreCustomerPasswordChangeService>();

var identityBuilder = builder.Services.AddIdentityCore<ApplicationUser>(
    options =>
    {
        options.User.RequireUniqueEmail = false;

        options.Lockout.AllowedForNewUsers = true;

        options.Lockout.MaxFailedAccessAttempts = 5;

        options.Lockout.DefaultLockoutTimeSpan =
            TimeSpan.FromMinutes(5);
    });

identityBuilder
    .AddUserStore<ScopedUserStoreFacade>()
    .AddTokenProvider<DataProtectorTokenProvider<ApplicationUser>>(
        TokenOptions.DefaultProvider)
    .AddSignInManager();

// Replace the default principal factory with the RetailFlow factory.
builder.Services.Replace(
    ServiceDescriptor.Scoped<
        IUserClaimsPrincipalFactory<ApplicationUser>,
        StoreCustomerClaimsPrincipalFactory>());

// Validate SecurityStamp within the 15-minute experimental cookie lifetime.
builder.Services.Configure<SecurityStampValidatorOptions>(options =>
{
    options.ValidationInterval = TimeSpan.FromMinutes(5);
});

builder.Services
    .AddOptions<BrowserCsrfProtectionOptions>()
    .BindConfiguration(BrowserCsrfProtectionOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<
    IValidateOptions<BrowserCsrfProtectionOptions>,
    BrowserCsrfProtectionOptionsValidator>();

// -----------------------------------------------------------------------------
// StoreCustomer Authentication
// -----------------------------------------------------------------------------

builder.Services
    .AddOptions<SessionLifetimeOptions>()
    .BindConfiguration(SessionLifetimeOptions.SectionName)
    .Validate(
        options => options.IdleTimeout > TimeSpan.Zero,
        "Session idle timeout must be greater than zero.")
    .Validate(
        options => options.AbsoluteLifetime > options.IdleTimeout,
        "Absolute session lifetime must be greater than idle timeout.")
    .ValidateOnStart();

builder.Services.AddScoped<
    StoreCustomerCookieAuthenticationEvents>();

builder.Services
    .AddAuthentication(options =>
    {
        // SignInManager<ApplicationUser> defaults to Identity.Application.
        options.DefaultAuthenticateScheme =
            IdentityConstants.ApplicationScheme;

        options.DefaultChallengeScheme =
            IdentityConstants.ApplicationScheme;

        options.DefaultSignInScheme =
            IdentityConstants.ApplicationScheme;
    })
    .AddCookie(
        IdentityConstants.ApplicationScheme,
        options =>
        {
            options.Cookie.Name =
                StoreCustomerCookieManager.BaseCookieName;

            // StoreCustomerCookieManager replaces this per request.
            options.Cookie.Path = "/";

            options.Cookie.HttpOnly = true;

            options.Cookie.SecurePolicy =
                CookieSecurePolicy.Always;

            options.Cookie.SameSite =
                SameSiteMode.Lax;

            options.Cookie.IsEssential = true;

            options.CookieManager =
                new StoreCustomerCookieManager();

            options.EventsType =
                typeof(StoreCustomerCookieAuthenticationEvents);

            options.SlidingExpiration = true;
        })
    .AddCookie(IdentityConstants.TwoFactorRememberMeScheme);

// -----------------------------------------------------------------------------
// Server-side Authentication Ticket Store
// -----------------------------------------------------------------------------

builder.Services.AddSingleton<TimeProvider>(
    TimeProvider.System);

builder.Services.AddSingleton<IStoreCustomerSessionOperationHook>(
    NoOpStoreCustomerSessionOperationHook.Instance);

builder.Services.AddSingleton<
    ITicketStore,
    SqlServerTicketStore>();

builder.Services
    .AddOptions<CookieAuthenticationOptions>(
        IdentityConstants.ApplicationScheme)
    .Configure<ITicketStore, TimeProvider>(
        (options, ticketStore, timeProvider) =>
        {
            options.SessionStore = ticketStore;

            // CookieAuthenticationHandler and SqlServerTicketStore
            // must use the same clock.
            options.TimeProvider = timeProvider;
        })
    .Configure<IOptions<SessionLifetimeOptions>>(
        (options, sessionLifetimeOptions) =>
        {
            // Idle timeout remains controlled by the configured policy.
            options.ExpireTimeSpan =
                sessionLifetimeOptions.Value.IdleTimeout;
        });

// -----------------------------------------------------------------------------
// Expired Session Cleanup
// -----------------------------------------------------------------------------

builder.Services.AddSingleton<ExpiredSessionTicketCleanupService>();

builder.Services.AddHostedService(
    services => services.GetRequiredService<
        ExpiredSessionTicketCleanupService>());

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        RetailFlowAuthorizationPolicies.StoreCustomerScope,
        policy =>
        {
            policy.RequireAuthenticatedUser();

            policy.AddRequirements(
                new StoreCustomerScopeRequirement());
        });
});

builder.Services.AddScoped<
    IAuthorizationHandler,
    StoreCustomerScopeAuthorizationHandler>();

// -----------------------------------------------------------------------------
// Build Application
// -----------------------------------------------------------------------------

var app = builder.Build();

// -----------------------------------------------------------------------------
// HTTP Pipeline
// -----------------------------------------------------------------------------

app.UseHttpsRedirection();

app.UseRouting();

// Normalize StoreCustomer URLs before CSRF and identity scope resolution.
app.UseMiddleware<CanonicalStoreSlugMiddleware>();

// Enforce browser CSRF protection on state-changing requests before identity scope/auth.
app.UseMiddleware<BrowserCsrfProtectionMiddleware>();

// This MUST execute before UseAuthentication.
app.UseMiddleware<IdentityScopeMiddleware>();

app.UseAuthentication();

app.UseAuthorization();

// -----------------------------------------------------------------------------
// StoreCustomer Route Group
// -----------------------------------------------------------------------------

var storeCustomerGroup = app
    .MapGroup("/api/stores/{slug}/customers")
    .WithMetadata(
        new IdentityScopeMetadata(
            AccountKind.StoreCustomer));

// Precompute a dummy hash using the configured hasher to equalize login timing
// when a user does not exist in the current store scope.
var dummyPasswordHash = new PasswordHasher<ApplicationUser>()
    .HashPassword(
        new ApplicationUser(),
        Guid.NewGuid().ToString("N"));

// -----------------------------------------------------------------------------
// Login
// -----------------------------------------------------------------------------

storeCustomerGroup.MapPost(
    "/login",
    async (
        HttpContext httpContext,
        CustomerLoginRequest request,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IPasswordHasher<ApplicationUser> passwordHasher) =>
    {
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            return Results.Problem(
                title: "An active session already exists.",
                detail: "Log out before switching to another customer account.",
                statusCode: StatusCodes.Status409Conflict);
        }

        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Results.Problem(
                title: "Invalid credentials.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // FindByEmailAsync is constrained by the current IdentityLookupScope.
        var user = await userManager.FindByEmailAsync(
            request.Email);

        if (user is null)
        {
            _ = passwordHasher.VerifyHashedPassword(
                new ApplicationUser(),
                dummyPasswordHash,
                request.Password);

            return Results.Problem(
                title: "Invalid credentials.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        // Use the overload that accepts the already scoped User.
        // Do not search again through the global username lookup.
        SignInResult result;

        try
        {
            result = await signInManager.PasswordSignInAsync(
                user,
                request.Password,
                isPersistent: false,
                lockoutOnFailure: true);
        }
        catch (StaleSecurityStampException)
        {
            return Results.Problem(
                title: "Invalid credentials.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!result.Succeeded)
        {
            return Results.Problem(
                title: "Invalid credentials.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        return Results.NoContent();
    });

// -----------------------------------------------------------------------------
// Current Authenticated Customer
// -----------------------------------------------------------------------------

storeCustomerGroup.MapGet(
    "/me",
    (System.Security.Claims.ClaimsPrincipal principal) =>
    {
        return Results.Ok(new
        {
            UserId = principal.FindFirst(
                System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,

            AccountKind = principal.FindFirst(
                RetailFlowClaimTypes.AccountKind)?.Value,

            StoreId = principal.FindFirst(
                RetailFlowClaimTypes.StoreId)?.Value
        });
    })
    .RequireAuthorization(
        RetailFlowAuthorizationPolicies.StoreCustomerScope);

// -----------------------------------------------------------------------------
// Logout
// -----------------------------------------------------------------------------

storeCustomerGroup.MapPost(
    "/logout",
    async (SignInManager<ApplicationUser> signInManager) =>
    {
        await signInManager.SignOutAsync();

        return Results.NoContent();
    })
    .RequireAuthorization(
        RetailFlowAuthorizationPolicies.StoreCustomerScope);

// -----------------------------------------------------------------------------
// Change Password & Revoke Sessions
// -----------------------------------------------------------------------------

storeCustomerGroup.MapPost(
    "/password/change",
    async (
        CustomerChangePasswordRequest request,
        HttpContext httpContext,
        UserManager<ApplicationUser> userManager,
        IStoreCustomerPasswordChangeService passwordChangeService,
        SignInManager<ApplicationUser> signInManager,
        CancellationToken cancellationToken) =>
    {
        // ---------------------------------------------------------
        // 1. Validate request.
        // ---------------------------------------------------------

        var validationErrors =
            new Dictionary<string, string[]>();

        if (string.IsNullOrEmpty(request.CurrentPassword))
        {
            validationErrors[nameof(request.CurrentPassword)] =
            [
                "Current password is required."
            ];
        }

        if (string.IsNullOrEmpty(request.NewPassword))
        {
            validationErrors[nameof(request.NewPassword)] =
            [
                "New password is required."
            ];
        }

        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(
                validationErrors,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid password change request.");
        }

        // ---------------------------------------------------------
        // 2. Resolve the authenticated user from the Principal.
        // ---------------------------------------------------------

        var userId = userManager.GetUserId(httpContext.User);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return Results.Unauthorized();
        }

        var user = await userManager.FindByIdAsync(userId);

        if (user is null)
        {
            return Results.Unauthorized();
        }

        // ---------------------------------------------------------
        // 3. Change password and revoke all user sessions atomically.
        // ---------------------------------------------------------

        var result =
            await passwordChangeService.ChangePasswordAndRevokeSessionsAsync(
                user,
                request.CurrentPassword,
                request.NewPassword,
                cancellationToken);

        if (!result.Succeeded)
        {
            var errors = result.Errors
                .GroupBy(
                    error => error.Code == "PasswordMismatch"
                        ? "CurrentPassword"
                        : "NewPassword",
                    StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(error => error.Description)
                        .ToArray(),
                    StringComparer.Ordinal);

            return Results.ValidationProblem(
                errors,
                statusCode: StatusCodes.Status400BadRequest,
                title: "Password change failed.");
        }

        // ---------------------------------------------------------
        // 4. Clear the current browser authentication cookie.
        //
        // All SQL tickets were already revoked by the service.
        // This call also emits the browser cookie deletion header.
        // ---------------------------------------------------------

        await signInManager.SignOutAsync();

        return Results.NoContent();
    })
    .RequireAuthorization(
        RetailFlowAuthorizationPolicies.StoreCustomerScope);

// -----------------------------------------------------------------------------
// Run
// -----------------------------------------------------------------------------

app.Run();

public sealed class SessionTransportProgramMarker
{
}
