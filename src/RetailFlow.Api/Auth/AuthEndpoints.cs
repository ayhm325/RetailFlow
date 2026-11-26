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

        storeAuth.MapPost("/forgot-password", RequestStorePasswordResetAsync)
            .WithName("ForgotStorePassword");

        storeAuth.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetStorePassword");

        storeAuth.MapPost("/confirm-email", ConfirmEmailAsync)
            .WithName("ConfirmStoreEmail");

        storeAuth.MapPost("/resend-confirmation", ResendStoreConfirmationAsync)
            .WithName("ResendStoreConfirmation");

        storeAuth.MapPost("/2fa/setup", BeginStoreTwoFactorSetupAsync)
            .RequireAuthorization()
            .WithName("SetupStoreTwoFactor");

        storeAuth.MapPost("/2fa/enable", EnableTwoFactorAsync)
            .RequireAuthorization()
            .WithName("EnableStoreTwoFactor");

        storeAuth.MapPost("/2fa/replace/setup", BeginStoreTwoFactorReplacementAsync)
            .RequireAuthorization()
            .WithName("BeginReplaceStoreTwoFactor");

        storeAuth.MapPost("/2fa/replace/confirm", ConfirmTwoFactorReplacementAsync)
            .RequireAuthorization()
            .WithName("ConfirmReplaceStoreTwoFactor");

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
            .RequireAuthorization("PlatformAdminEnrollment")
            .WithName("LogoutPlatformAdmin");

        platformAuth.MapPost("/forgot-password", RequestPlatformPasswordResetAsync)
            .WithName("ForgotPlatformPassword");

        platformAuth.MapPost("/reset-password", ResetPasswordAsync)
            .WithName("ResetPlatformPassword");

        platformAuth.MapPost("/2fa/setup", BeginPlatformTwoFactorSetupAsync)
            .RequireAuthorization("PlatformAdminEnrollment")
            .WithName("SetupPlatformTwoFactor");

        platformAuth.MapPost("/2fa/enable", EnableTwoFactorAsync)
            .RequireAuthorization("PlatformAdminEnrollment")
            .WithName("EnablePlatformTwoFactor");

        platformAuth.MapPost("/2fa/replace/setup", BeginPlatformTwoFactorReplacementAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("BeginReplacePlatformTwoFactor");

        platformAuth.MapPost("/2fa/replace/confirm", ConfirmTwoFactorReplacementAsync)
            .RequireAuthorization("PlatformAdmin")
            .WithName("ConfirmReplacePlatformTwoFactor");

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

        if (result.Outcome == AuthSignInOutcome.MfaEnrollmentRequired && result.User is not null)
        {
            logger.LogInformation("Audit: PlatformAdminMfaEnrollmentRequired UserId={UserId}", result.User.UserId);
            return TypedResults.Ok(result.User);
        }

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

    private static async Task<IResult> RequestStorePasswordResetAsync(
        ForgotPasswordCommand command,
        IStoreRequestContextAccessor storeContextAccessor,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        await authService.RequestPasswordResetAsync(command.Email, store.StoreSlug, cancellationToken);
        return TypedResults.Accepted((string?)null, new { message = "If the account exists, a password reset instruction has been sent." });
    }

    private static async Task<IResult> RequestPlatformPasswordResetAsync(
        ForgotPasswordCommand command,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        await authService.RequestPasswordResetAsync(command.Email, null, cancellationToken);
        return TypedResults.Accepted((string?)null, new { message = "If the account exists, a password reset instruction has been sent." });
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

    private static async Task<IResult> ConfirmEmailAsync(
        ConfirmEmailCommand command,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var confirmed = await authService.ConfirmEmailAsync(command, cancellationToken);
        return confirmed
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Email confirmation failed",
                detail: "The email confirmation token is invalid or expired.");
    }

    private static async Task<IResult> ResendStoreConfirmationAsync(
        ResendConfirmationCommand command,
        IStoreRequestContextAccessor storeContextAccessor,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        var store = storeContextAccessor.Current
            ?? throw new InvalidOperationException("Store context was not resolved.");

        await authService.RequestEmailConfirmationAsync(command.Email, store.StoreSlug, cancellationToken);
        return TypedResults.Accepted((string?)null, new { message = "If the account is eligible, a confirmation email has been sent." });
    }

    private static async Task<IResult> BeginStoreTwoFactorSetupAsync(
        BeginTwoFactorSetupCommand command,
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
        var setup = await authService.BeginTwoFactorSetupAsync(userId, issuer, command, cancellationToken);
        return setup is not null
            ? TypedResults.Ok(setup)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Two-factor setup rejected",
                detail: "Two-factor setup could not be initiated. Ensure your password is correct and MFA is not already enrolled.");
    }

    private static async Task<IResult> BeginPlatformTwoFactorSetupAsync(
        BeginTwoFactorSetupCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var setup = await authService.BeginTwoFactorSetupAsync(userId, "RetailFlow Platform", command, cancellationToken);
        return setup is not null
            ? TypedResults.Ok(setup)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Two-factor setup rejected",
                detail: "Two-factor setup could not be initiated. Ensure your password is correct and MFA is not already enrolled.");
    }

    private static async Task<IResult> EnableTwoFactorAsync(
        EnableTwoFactorCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var recovery = await authService.EnableTwoFactorAsync(userId, command, cancellationToken);
        return recovery is not null
            ? TypedResults.Ok(recovery)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid verification code",
                detail: "The two-factor verification code is invalid, expired, or setup is not pending.");
    }

    private static async Task<IResult> BeginStoreTwoFactorReplacementAsync(
        BeginTwoFactorReplacementCommand command,
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
        var setup = await authService.BeginTwoFactorReplacementAsync(userId, issuer, command, cancellationToken);
        return setup is not null
            ? TypedResults.Ok(setup)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Authenticator replacement rejected",
                detail: "Step-up verification failed or MFA is not currently enabled.");
    }

    private static async Task<IResult> BeginPlatformTwoFactorReplacementAsync(
        BeginTwoFactorReplacementCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var setup = await authService.BeginTwoFactorReplacementAsync(userId, "RetailFlow Platform", command, cancellationToken);
        return setup is not null
            ? TypedResults.Ok(setup)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Authenticator replacement rejected",
                detail: "Step-up verification failed or MFA is not currently enabled.");
    }

    private static async Task<IResult> ConfirmTwoFactorReplacementAsync(
        ConfirmTwoFactorReplacementCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var recovery = await authService.ConfirmTwoFactorReplacementAsync(userId, command, cancellationToken);
        return recovery is not null
            ? TypedResults.Ok(recovery)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Authenticator replacement failed",
                detail: "The verification code is invalid, expired, or replacement is not pending.");
    }

    private static async Task<IResult> DisableTwoFactorAsync(
        DisableTwoFactorCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var disabled = await authService.DisableTwoFactorAsync(userId, command, cancellationToken);
        return disabled
            ? TypedResults.NoContent()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Disable two-factor failed",
                detail: "Step-up verification failed or MFA is not enabled.");
    }

    private static async Task<IResult> RegenerateRecoveryCodesAsync(
        RegenerateRecoveryCodesCommand command,
        ClaimsPrincipal principal,
        IAuthService authService,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return TypedResults.Unauthorized();
        }

        var recovery = await authService.RegenerateRecoveryCodesAsync(userId, command, cancellationToken);
        return recovery is not null
            ? TypedResults.Ok(recovery)
            : TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Recovery code regeneration failed",
                detail: "Step-up TOTP verification failed or MFA is not enabled.");
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
