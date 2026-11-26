using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Identity;
using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly RetailFlowDbContext _dbContext;

    public AuthService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AuthenticatedUserDto> RegisterCustomerAsync(
        Guid storeId,
        RegisterCustomerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var hash = PasswordHasherService.Hash(command.Password);
        var user = AppUser.CreateStoreUser(
            Guid.NewGuid(),
            storeId,
            command.Email,
            command.DisplayName,
            hash,
            AppRole.Customer,
            DateTimeOffset.UtcNow);

        _dbContext.AppUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(user);
    }

    public async Task<AuthenticatedUserDto> CreateMerchantOwnerAsync(
        Guid storeId,
        CreateMerchantUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var hash = PasswordHasherService.Hash(command.Password);
        var user = AppUser.CreateStoreUser(
            Guid.NewGuid(),
            storeId,
            command.Email,
            command.DisplayName,
            hash,
            AppRole.MerchantOwner,
            DateTimeOffset.UtcNow);

        _dbContext.AppUsers.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(user);
    }

    public async Task<AuthenticatedUserDto?> AuthenticateStoreUserAsync(
        Guid storeId,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var user = await _dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.StoreId == storeId && u.NormalizedEmail == normalizedEmail && u.IsActive,
                cancellationToken);

        if (user is null || !PasswordHasherService.Verify(command.Password, user.PasswordHash))
        {
            return null;
        }

        return Map(user);
    }

    public async Task<AuthenticatedUserDto?> AuthenticatePlatformAdminAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var user = await _dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.StoreId == null && u.Role == AppRole.PlatformAdmin && u.NormalizedEmail == normalizedEmail && u.IsActive,
                cancellationToken);

        if (user is null || !PasswordHasherService.Verify(command.Password, user.PasswordHash))
        {
            return null;
        }

        return Map(user);
    }

    public async Task<Guid?> EnsurePlatformAdminAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var hasPlatformAdmin = await _dbContext.AppUsers
            .AsNoTracking()
            .AnyAsync(u => u.Role == AppRole.PlatformAdmin, cancellationToken);

        if (hasPlatformAdmin)
        {
            return null;
        }

        var hash = PasswordHasherService.Hash(password);
        var admin = AppUser.CreatePlatformAdmin(
            Guid.NewGuid(),
            email,
            displayName,
            hash,
            DateTimeOffset.UtcNow);

        _dbContext.AppUsers.Add(admin);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return admin.Id;
    }

    private static AuthenticatedUserDto Map(AppUser user) =>
        new(user.Id, user.StoreId, user.Email, user.DisplayName, user.Role);
}
