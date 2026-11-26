using System.Security.Claims;

using RetailFlow.Api.Catalog;
using RetailFlow.Application.Identity;
using RetailFlow.Application.Stores;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.Auth;

public static class AuthEndpoints
{
    public static WebApplication MapAuthEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var storeAuth = app.MapGroup("/api/stores/{slug}/auth")
            .WithMetadata(new IdentityScopeMetadata(IdentityScopeType.Store))
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

        storeAuth.MapPost("/forgot-password", RequestPasswordResetAsync)
            .WithName("ForgotStorePassword");

        storeAuth.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetStorePassword");

        storeAuth.MapPost("/2fa/setup", BeginTwoFactorSetupAsync)
            .RequireAuthorization()
            .WithName("SetupStoreTwoFactor");

        storeAuth.MapPost("/2fa/enable", EnableTwoFactorAsync)
            .RequireAuthorization()
            .WithName("EnableStoreTwoFactor");

        storeAuth.MapPost("/2fa/disable", DisableTwoFactorAsync)
            .RequireAuthorization()
            .WithName("DisableStoreTwoFactor");

        storeAuth.MapPost("/2fa/recovery-codes", RegenerateRecoveryCodesAsync)
            .RequireAuthorization()
            .WithName("RegenerateStoreRecoveryCodes");

        var platformAuth = app.MapGroup("/api/platform/auth")
            .WithMetadata(new IdentityScopeMetadata(IdentityScopeType.Platform));

        platformAuth.MapPost("/login", LoginPlatformAdminAsync)
            .WithName("LoginPlatformAdmin");

        platformAuth.MapPost("/logout", (Delegate)LogoutAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("LogoutPlatformAdmin");

        platformAuth.MapPost("/2fa/setup", BeginPlatformTwoFactorSetupAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("SetupPlatformTwoFactor");

        platformAuth.MapPost("/2fa/enable", EnableTwoFactorAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("EnablePlatformTwoFactor");

        platformAuth.MapPost("/2fa/disable", DisableTwoFactorAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("DisablePlatformTwoFactor");

        platformAuth.MapPost("/2fa/recovery-codes", RegenerateRecoveryCodesAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("RegeneratePlatformRecoveryCodes");

        return app;
    }

    private static async Task<IResult> RegisterCustomerAsync(
        RegisterCustomerCommand command,
        IStoreRequestContextAccessor storeContextAccessor,
        IAuthService authService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var user = await authService.RegisterCustomerAsync(store.StoreId, command, cancellationToken);
        loggerFactory.CreateLogger("RetailFlow.Audit.Auth")
            .LogInformation("Audit: CustomerRegistered StoreId={StoreId} UserId={UserId}", store.StoreId, user.UserId);
        return TypedResults.Created($"/api/stores/{store.StoreSlug}/auth/me", user);
    }

    private static async Task<IResult> LoginStoreUserAsync(
        LoginCommand command,
        IStoreRequestContextAccessor storeContextAccessor,
        IAuthService authService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        var logger = loggerFactory.CreateLogger("RetailFlow.Audit.Auth");
        var result = await authService.SignInStoreUserAsync(store.StoreId, command, cancellationToken);
        if (result.Outcome != AuthSignInOutcome.Succeeded || result.User is null)
        {
            logger.LogWarning("Audit: StoreLoginFailed StoreId={StoreId} Outcome={Outcome}", store.StoreId, result.Outcome);
            return MapFailedSignIn(result.Outcome);
        }

        logger.LogInformation(
            "Audit: StoreLoginSucceeded StoreId={StoreId} UserId={UserId} Role={Role}",
            store.StoreId,
            result.User.UserId,
            result.User.Role);
        return TypedResults.Ok(result.User);
    }

    private static async Task<IResult> LoginPlatformAdminAsync(
        LoginCommand command,
        IAuthService authService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("RetailFlow.Audit.Auth");
        var result = await authService.SignInPlatformAdminAsync(command, cancellationToken);
        if (result.Outcome != AuthSignInOutcome.Succeeded || result.User is null)
        {
            logger.LogWarning("Audit: PlatformAdminLoginFailed Outcome={Outcome}", result.Outcome);
            return MapFailedSignIn(result.Outcome);
        }

        logger.LogInformation("Audit: PlatformAdminLoginSucceeded UserId={UserId}", result.User.UserId);
        return TypedResults.Ok(result.User);
    }

    private static async Task<IResult> LogoutAsync(
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        await authService.SignOutAsync(cancellationToken);
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

    private static async Task<IResult> RequestPasswordResetAsync(
        ForgotPasswordCommand command,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(command.Email, cancellationToken);
        return TypedResults.Accepted((string?)null, new { message = "If the account exists, a password reset instruction has been queued." });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordCommand command,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var succeeded = await authService.ResetPasswordAsync(command, cancellationToken);
        return succeeded
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Password reset failed",
                detail: "The password reset request could not be completed.");
    }

    private static async Task<IResult> BeginTwoFactorSetupAsync(
        IStoreRequestContextAccessor storeContextAccessor,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var store = storeContextAccessor.Current;
        var issuer = store is not null ? $"RetailFlow ({store.StoreSlug})" : "RetailFlow";
        var setup = await authService.BeginTwoFactorSetupAsync(userId, issuer, cancellationToken);
        return TypedResults.Ok(setup);
    }

    private static async Task<IResult> BeginPlatformTwoFactorSetupAsync(
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var setup = await authService.BeginTwoFactorSetupAsync(userId, "RetailFlow Platform", cancellationToken);
        return TypedResults.Ok(setup);
    }

    private static async Task<IResult> EnableTwoFactorAsync(
        TwoFactorVerifyCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var recovery = await authService.EnableTwoFactorAsync(userId, command.Code, cancellationToken);
        return recovery is not null
            ? TypedResults.Ok(recovery)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid verification code",
                detail: "The two-factor verification code is invalid.");
    }

    private static async Task<IResult> DisableTwoFactorAsync(
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var disabled = await authService.DisableTwoFactorAsync(userId, cancellationToken);
        return disabled ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var recovery = await authService.RegenerateRecoveryCodesAsync(userId, cancellationToken);
        return recovery is not null ? TypedResults.Ok(recovery) : TypedResults.BadRequest();
    }

    private static IResult MapFailedSignIn(AuthSignInOutcome outcome) => outcome switch
    {
        AuthSignInOutcome.LockedOut => TypedResults.Problem(
            statusCode: StatusCodes.Status423Locked,
            title: "Account locked",
            detail: "Account is temporarily locked due to repeated failed sign-in attempts."),
        AuthSignInOutcome.RequiresTwoFactor => TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Two-factor authentication required",
            detail: "A valid two-factor authentication code or recovery code is required."),
        _ => TypedResults.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials",
            detail: "Email or password is invalid.")
    };
}
