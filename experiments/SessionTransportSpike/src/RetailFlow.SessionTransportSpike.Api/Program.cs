using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

using RetailFlow.SessionTransportSpike.Api.Authentication;
using RetailFlow.SessionTransportSpike.Api.Contracts;
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
// Data Protection
// -----------------------------------------------------------------------------

builder.Services.AddDataProtection();

// -----------------------------------------------------------------------------
// ASP.NET Core Identity
// -----------------------------------------------------------------------------

builder.Services.AddScoped<ScopedUserStore>();

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

            // Experimental idle lifetime only.
            // This is not yet the final RetailFlow session policy.
            options.ExpireTimeSpan =
                TimeSpan.FromMinutes(15);

            options.SlidingExpiration = true;
        });

// -----------------------------------------------------------------------------
// Server-side Authentication Ticket Store
// -----------------------------------------------------------------------------

builder.Services.AddSingleton<
    ITicketStore,
    SqlServerTicketStore>();

builder.Services
    .AddOptions<CookieAuthenticationOptions>(
        IdentityConstants.ApplicationScheme)
    .Configure<ITicketStore>(
        (cookieOptions, ticketStore) =>
        {
            cookieOptions.SessionStore = ticketStore;
        });

builder.Services.AddAuthorization();

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
        var result = await signInManager.PasswordSignInAsync(
            user,
            request.Password,
            isPersistent: false,
            lockoutOnFailure: true);

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
    .RequireAuthorization();

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
    .RequireAuthorization();

// -----------------------------------------------------------------------------
// Run
// -----------------------------------------------------------------------------

app.Run();

public sealed class SessionTransportProgramMarker
{
}
