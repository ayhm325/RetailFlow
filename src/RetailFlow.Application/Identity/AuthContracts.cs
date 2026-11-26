using RetailFlow.Domain.Identity;

namespace RetailFlow.Application.Identity;

public sealed record RegisterCustomerCommand(string Email, string DisplayName, string Password);

public sealed record CreateMerchantUserCommand(string Email, string DisplayName, string Password);

public sealed record LoginCommand(string Email, string Password, string? TwoFactorCode = null, string? RecoveryCode = null);

public sealed record ForgotPasswordCommand(string Email);

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);

public sealed record ConfirmEmailCommand(Guid UserId, string Token);

public sealed record ResendConfirmationCommand(string Email);

public sealed record BeginTwoFactorSetupCommand(string CurrentPassword);

public sealed record EnableTwoFactorCommand(string Code);

public sealed record BeginTwoFactorReplacementCommand(
    string CurrentPassword,
    string? CurrentTwoFactorCode = null,
    string? RecoveryCode = null);

public sealed record ConfirmTwoFactorReplacementCommand(string NewCode);

public sealed record DisableTwoFactorCommand(
    string CurrentPassword,
    string? TwoFactorCode = null,
    string? RecoveryCode = null);

public sealed record RegenerateRecoveryCodesCommand(string TwoFactorCode);

public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri, DateTimeOffset ExpiresAtUtc);

public sealed record TwoFactorRecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

public sealed record AuthenticatedUserDto(
    Guid UserId,
    Guid? StoreId,
    string Email,
    string DisplayName,
    AppRole Role,
    bool MfaRequiredForPlatformAccess = false);

public enum AuthSignInOutcome
{
    Succeeded = 1,
    Failed = 2,
    LockedOut = 3,
    RequiresTwoFactor = 4,
    NotAllowed = 5,
    MfaEnrollmentRequired = 6
}

public sealed record AuthSignInResult(
    AuthSignInOutcome Outcome,
    AuthenticatedUserDto? User = null);

public interface IRetailFlowEmailSender
{
    Task SendPasswordResetAsync(
        string toEmail,
        string resetLink,
        CancellationToken cancellationToken);

    Task SendEmailConfirmationAsync(
        string toEmail,
        string confirmationLink,
        CancellationToken cancellationToken);

    Task SendSecurityNotificationAsync(
        string toEmail,
        string subject,
        string message,
        CancellationToken cancellationToken);
}

public interface IAuthService
{
    Task<AuthenticatedUserDto> RegisterCustomerAsync(
        Guid storeId,
        RegisterCustomerCommand command,
        CancellationToken cancellationToken);

    Task<AuthenticatedUserDto> CreateMerchantOwnerAsync(
        Guid storeId,
        CreateMerchantUserCommand command,
        CancellationToken cancellationToken);

    Task<AuthSignInResult> SignInStoreUserAsync(
        Guid storeId,
        LoginCommand command,
        CancellationToken cancellationToken);

    Task<AuthSignInResult> SignInPlatformAdminAsync(
        LoginCommand command,
        CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);

    Task<Guid?> EnsurePlatformAdminAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken);

    Task<bool> DeactivateUserAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task RequestPasswordResetAsync(
        string email,
        string? storeSlug,
        CancellationToken cancellationToken);

    Task<bool> ResetPasswordAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken);

    Task RequestEmailConfirmationAsync(
        string email,
        string? storeSlug,
        CancellationToken cancellationToken);

    Task<bool> ConfirmEmailAsync(
        ConfirmEmailCommand command,
        CancellationToken cancellationToken);

    Task<TwoFactorSetupDto?> BeginTwoFactorSetupAsync(
        Guid userId,
        string issuer,
        BeginTwoFactorSetupCommand command,
        CancellationToken cancellationToken);

    Task<TwoFactorRecoveryCodesDto?> EnableTwoFactorAsync(
        Guid userId,
        EnableTwoFactorCommand command,
        CancellationToken cancellationToken);

    Task<TwoFactorSetupDto?> BeginTwoFactorReplacementAsync(
        Guid userId,
        string issuer,
        BeginTwoFactorReplacementCommand command,
        CancellationToken cancellationToken);

    Task<TwoFactorRecoveryCodesDto?> ConfirmTwoFactorReplacementAsync(
        Guid userId,
        ConfirmTwoFactorReplacementCommand command,
        CancellationToken cancellationToken);

    Task<bool> DisableTwoFactorAsync(
        Guid userId,
        DisableTwoFactorCommand command,
        CancellationToken cancellationToken);

    Task<TwoFactorRecoveryCodesDto?> RegenerateRecoveryCodesAsync(
        Guid userId,
        RegenerateRecoveryCodesCommand command,
        CancellationToken cancellationToken);
}
