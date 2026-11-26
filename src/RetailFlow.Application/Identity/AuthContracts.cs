using RetailFlow.Domain.Identity;

namespace RetailFlow.Application.Identity;

public sealed record RegisterCustomerCommand(string Email, string DisplayName, string Password);

public sealed record CreateMerchantUserCommand(string Email, string DisplayName, string Password);

public sealed record LoginCommand(string Email, string Password);

public sealed record AuthenticatedUserDto(
    Guid UserId,
    Guid? StoreId,
    string Email,
    string DisplayName,
    AppRole Role);

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

    Task<AuthenticatedUserDto?> AuthenticateStoreUserAsync(
        Guid storeId,
        LoginCommand command,
        CancellationToken cancellationToken);

    Task<AuthenticatedUserDto?> AuthenticatePlatformAdminAsync(
        LoginCommand command,
        CancellationToken cancellationToken);

    Task<Guid?> EnsurePlatformAdminAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken);
}
