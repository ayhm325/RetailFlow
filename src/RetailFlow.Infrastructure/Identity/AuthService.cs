using System.Data;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

using RetailFlow.Application.Identity;
using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private readonly RetailFlowDbContext _dbContext;
    private readonly ILogger<AuthService> _logger;

    public AuthService(RetailFlowDbContext dbContext, ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
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

        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrEmpty(command.Password) ||
            command.Password.Length > PasswordHasherService.MaxPasswordLength)
        {
            return null;
        }

        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var user = await _dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.StoreId == storeId && u.NormalizedEmail == normalizedEmail && u.IsActive,
                cancellationToken);

        return await VerifyAndUpgradeHashAsync(user, command.Password, cancellationToken);
    }

    public async Task<AuthenticatedUserDto?> AuthenticatePlatformAdminAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrEmpty(command.Password) ||
            command.Password.Length > PasswordHasherService.MaxPasswordLength)
        {
            return null;
        }

        var normalizedEmail = command.Email.Trim().ToUpperInvariant();
        var user = await _dbContext.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.StoreId == null && u.Role == AppRole.PlatformAdmin && u.NormalizedEmail == normalizedEmail && u.IsActive,
                cancellationToken);

        return await VerifyAndUpgradeHashAsync(user, command.Password, cancellationToken);
    }

    public async Task<Guid?> EnsurePlatformAdminAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        var hash = PasswordHasherService.Hash(password);

        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            if (_dbContext.Database.IsSqlServer())
            {
                await _dbContext.Database.ExecuteSqlRawAsync(
                    "EXEC sp_getapplock @Resource = N'RetailFlow:PlatformBootstrap', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;",
                    cancellationToken);
            }

            // Check ALL PlatformAdmin rows including deactivated ones so we never provision a duplicate admin.
            var hasPlatformAdmin = await _dbContext.AppUsers
                .AsNoTracking()
                .AnyAsync(u => u.Role == AppRole.PlatformAdmin, cancellationToken);

            if (hasPlatformAdmin)
            {
                await tx.CommitAsync(cancellationToken);
                return null;
            }

            var admin = AppUser.CreatePlatformAdmin(
                Guid.NewGuid(),
                email,
                displayName,
                hash,
                DateTimeOffset.UtcNow);

            _dbContext.AppUsers.Add(admin);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return admin.Id;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
        {
            _logger.LogInformation("PlatformAdmin already provisioned concurrently; skipping bootstrap.");
            await tx.RollbackAsync(cancellationToken);
            return null;
        }
    }

    private async Task<AuthenticatedUserDto?> VerifyAndUpgradeHashAsync(
        AppUser? user,
        string password,
        CancellationToken cancellationToken)
    {
        if (user is null)
        {
            return null;
        }

        var verification = PasswordHasherService.Verify(password, user.PasswordHash);
        if (verification is PasswordHashVerificationResult.Failed or PasswordHashVerificationResult.InvalidHashFormat)
        {
            return null;
        }

        if (verification == PasswordHashVerificationResult.SuccessRehashNeeded)
        {
            try
            {
                var upgradedHash = PasswordHasherService.RehashVerifiedPassword(password);
                await _dbContext.AppUsers
                    .Where(u => u.Id == user.Id && u.PasswordHash == user.PasswordHash)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(u => u.PasswordHash, upgradedHash),
                        cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Never reject a valid login because a transparent hash upgrade could not be persisted.
                _logger.LogWarning(ex, "Failed to persist transparent password hash upgrade for UserId={UserId}.", user.Id);
            }
        }

        return Map(user);
    }

    private static AuthenticatedUserDto Map(AppUser user) =>
        new(user.Id, user.StoreId, user.Email, user.DisplayName, user.Role);
}
