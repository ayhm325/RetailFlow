using RetailFlow.Domain.Identity;

namespace RetailFlow.Application.Identity;

public sealed record RegisterCustomerCommand(string Email, string DisplayName, string Password);

public sealed record CreateMerchantUserCommand(string Email, string DisplayName, string Password);

public sealed record LoginCommand(string Email, string Password, string? TwoFactorCode = null, string? RecoveryCode = null);

public sealed record ForgotPasswordCommand(string Email);

public sealed record ResetPasswordCommand(string Email, string Token, string NewPassword);

public sealed record TwoFactorVerifyCommand(string Code);

public sealed record TwoFactorSetupDto(string SharedKey, string AuthenticatorUri);

public sealed record TwoFactorRecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

public sealed record AuthenticatedUserDto(
    Guid UserId,
    Guid? StoreId,
    string Email,
    string DisplayName,
    AppRole Role);

public enum AuthSignInOutcome
{
    Succeeded = 1,
    Failed = 2,
    LockedOut = 3,
    RequiresTwoFactor = 4,
    NotAllowed = 5
}

public sealed record AuthSignInResult(
    AuthSignInOutcome Outcome,
    AuthenticatedUserDto? User = null);

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
        CancellationToken cancellationToken);

    Task<bool> ResetPasswordAsync(
        ResetPasswordCommand command,
        CancellationToken cancellationToken);

    Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(
        Guid userId,
        string issuer,
        CancellationToken cancellationToken);

    Task<TwoFactorRecoveryCodesDto?> EnableTwoFactorAsync(
        Guid userId,
        string verificationCode,
        CancellationToken cancellationToken);

    Task<bool> DisableTwoFactorAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<TwoFactorRecoveryCodesDto?> RegenerateRecoveryCodesAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
