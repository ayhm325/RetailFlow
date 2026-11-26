using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

using RetailFlow.Api.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;

namespace RetailFlow.Api.Auth;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var storeAuth = app.MapGroup("/api/stores/{slug}/auth")
            .AddEndpointFilter<StoreResolutionEndpointFilter>();

        storeAuth.MapPost("/register", RegisterCustomerAsync)
            .WithName("RegisterStoreCustomer");

        storeAuth.MapPost("/login", LoginStoreUserAsync)
            .WithName("LoginStoreUser");

        storeAuth.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization()
            .WithName("LogoutStoreUser");

        storeAuth.MapGet("/me", GetCurrentUserAsync)
            .RequireAuthorization()
            .WithName("GetCurrentStoreUser");

        var platformAuth = app.MapGroup("/api/platform/auth");

        platformAuth.MapPost("/login", LoginPlatformAdminAsync)
            .WithName("LoginPlatformAdmin");

        platformAuth.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("LogoutPlatformAdmin");

        return app;
    }

    private static async Task<IResult> RegisterCustomerAsync(
        RegisterCustomerCommand command,
        IStoreRequestContextAccessor storeContextAccessor,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var user = await authService.RegisterCustomerAsync(store.StoreId, command, cancellationToken);
        await SignInAsync(httpContext, user);
        return TypedResults.Created($"/api/stores/{store.StoreSlug}/auth/me", user);
    }

    private static async Task<IResult> LoginStoreUserAsync(
        LoginCommand command,
        IStoreRequestContextAccessor storeContextAccessor,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var user = await authService.AuthenticateStoreUserAsync(store.StoreId, command, cancellationToken);
        if (user is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "Email or password is invalid.");
        }

        await SignInAsync(httpContext, user);
        return TypedResults.Ok(user);
    }

    private static async Task<IResult> LoginPlatformAdminAsync(
        LoginCommand command,
        IAuthService authService,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var user = await authService.AuthenticatePlatformAdminAsync(command, cancellationToken);
        if (user is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Invalid credentials",
                detail: "Email or password is invalid.");
        }

        await SignInAsync(httpContext, user);
        return TypedResults.Ok(user);
    }

    private static async Task<IResult> LogoutAsync(HttpContext httpContext)
    {
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return TypedResults.NoContent();
    }

    private static IResult GetCurrentUserAsync(
        IStoreRequestContextAccessor storeContextAccessor,
        ClaimsPrincipal principal)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var storeIdClaim = principal.FindFirstValue("store_id");
        if (!Guid.TryParse(storeIdClaim, out var claimStoreId) || claimStoreId != store.StoreId)
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(new
        {
            UserId = principal.FindFirstValue(ClaimTypes.NameIdentifier),
            StoreId = claimStoreId,
            Email = principal.FindFirstValue(ClaimTypes.Email),
            DisplayName = principal.FindFirstValue(ClaimTypes.Name),
            Role = principal.FindFirstValue(ClaimTypes.Role)
        });
    }

    private static async Task SignInAsync(HttpContext httpContext, AuthenticatedUserDto user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Name, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        if (user.StoreId.HasValue)
        {
            claims.Add(new Claim("store_id", user.StoreId.Value.ToString()));
        }

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
    }
}
