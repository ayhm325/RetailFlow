using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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
    private readonly UserManager<RetailFlowIdentityUser> _userManager;
    private readonly RoleManager<RetailFlowIdentityRole> _roleManager;
    private readonly SignInManager<RetailFlowIdentityUser> _signInManager;
    private readonly IPasswordHasher<RetailFlowIdentityUser> _passwordHasher;
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        RetailFlowDbContext dbContext,
        UserManager<RetailFlowIdentityUser> userManager,
        RoleManager<RetailFlowIdentityRole> roleManager,
        SignInManager<RetailFlowIdentityUser> signInManager,
        IPasswordHasher<RetailFlowIdentityUser> passwordHasher,
        IIdentityLookupScopeAccessor scopeAccessor,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider timeProvider,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _userManager = userManager;
        _roleManager = roleManager;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher;
        _scopeAccessor = scopeAccessor;
        _httpContextAccessor = httpContextAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthenticatedUserDto> RegisterCustomerAsync(
        Guid storeId,
        RegisterCustomerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Password) ||
            command.Password.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            throw new ArgumentException("Password length is invalid.", nameof(command));
        }

        EnsureScopeMatches(IdentityLookupScope.ForStore(storeId));
        await EnsureSystemRolesSeededAsync();

        var userId = Guid.NewGuid();
        var user = new RetailFlowIdentityUser
        {
            Id = userId,
            UserName = userId.ToString("N"),
            Email = command.Email?.Trim() ?? string.Empty,
            DisplayName = command.DisplayName?.Trim() ?? string.Empty,
            StoreId = storeId,
            IsActive = true,
            EmailConfirmed = false,
            CreatedAtUtc = _timeProvider.GetUtcNow()
        };

        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var createResult = await _userManager.CreateAsync(user, command.Password);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(BuildSafeIdentityError(createResult));
        }

        var roleResult = await _userManager.AddToRoleAsync(user, IdentityRoleNames.Customer);
        if (!roleResult.Succeeded)
        {
            throw new InvalidOperationException(BuildSafeIdentityError(roleResult));
        }

        await tx.CommitAsync(cancellationToken);
        if (_httpContextAccessor.HttpContext is not null)
        {
            await _signInManager.SignInAsync(user, isPersistent: true);
        }

        return new AuthenticatedUserDto(user.Id, user.StoreId, user.Email!, user.DisplayName, AppRole.Customer);
    }

    public async Task<AuthenticatedUserDto> CreateMerchantOwnerAsync(
        Guid storeId,
        CreateMerchantUserCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (storeId == Guid.Empty)
        {
            throw new ArgumentException("StoreId is required.", nameof(storeId));
        }

        if (string.IsNullOrWhiteSpace(command.Password) ||
            command.Password.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            throw new ArgumentException("Password length is invalid.", nameof(command));
        }

        // PlatformAdmin calls /api/platform/stores/{storeId}/merchant-owner under Platform scope;
        // switch temporarily to the target Store scope for tenant-scoped user creation.
        var previousScope = _scopeAccessor.Current;
        _scopeAccessor.SetCurrent(IdentityLookupScope.ForStore(storeId));

        try
        {
            await EnsureSystemRolesSeededAsync();

            var userId = Guid.NewGuid();
            var user = new RetailFlowIdentityUser
            {
                Id = userId,
                UserName = userId.ToString("N"),
                Email = command.Email?.Trim() ?? string.Empty,
                DisplayName = command.DisplayName?.Trim() ?? string.Empty,
                StoreId = storeId,
                IsActive = true,
                EmailConfirmed = false,
                CreatedAtUtc = _timeProvider.GetUtcNow()
            };

            await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var createResult = await _userManager.CreateAsync(user, command.Password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(BuildSafeIdentityError(createResult));
            }

            var roleResult = await _userManager.AddToRoleAsync(user, IdentityRoleNames.MerchantOwner);
            if (!roleResult.Succeeded)
            {
                throw new InvalidOperationException(BuildSafeIdentityError(roleResult));
            }

            await tx.CommitAsync(cancellationToken);

            return new AuthenticatedUserDto(user.Id, user.StoreId, user.Email!, user.DisplayName, AppRole.MerchantOwner);
        }
        finally
        {
            if (previousScope is not null)
            {
                _scopeAccessor.SetCurrent(previousScope);
            }
            else
            {
                _scopeAccessor.Clear();
            }
        }
    }

    public async Task<AuthSignInResult> SignInStoreUserAsync(
        Guid storeId,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrEmpty(command.Password) ||
            command.Password.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        EnsureScopeMatches(IdentityLookupScope.ForStore(storeId));

        var user = await _userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null || !user.IsActive || user.StoreId != storeId)
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        var roles = await _userManager.GetRolesAsync(user);
        var role = ResolveSingleStoreRole(roles);
        if (role is null)
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        return await VerifyPasswordAndSignInAsync(user, role.Value, command, cancellationToken);
    }

    public async Task<AuthSignInResult> SignInPlatformAdminAsync(
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrEmpty(command.Password) ||
            command.Password.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        EnsureScopeMatches(IdentityLookupScope.ForPlatform());

        var user = await _userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null || !user.IsActive || user.StoreId is not null)
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        if (!await _userManager.IsInRoleAsync(user, IdentityRoleNames.PlatformAdmin))
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        return await VerifyPasswordAndSignInAsync(user, AppRole.PlatformAdmin, command, cancellationToken);
    }

    public Task SignOutAsync(CancellationToken cancellationToken) =>
        _signInManager.SignOutAsync();

    public async Task<Guid?> EnsurePlatformAdminAsync(
        string email,
        string displayName,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password) || password.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            throw new ArgumentException("PlatformAdmin bootstrap password length is invalid.", nameof(password));
        }

        var previousScope = _scopeAccessor.Current;
        _scopeAccessor.SetCurrent(IdentityLookupScope.ForSystemProvisioning());

        try
        {
            await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                if (_dbContext.Database.IsSqlServer())
                {
                    await _dbContext.Database.ExecuteSqlRawAsync(
                        "EXEC sp_getapplock @Resource = N'RetailFlow:PlatformBootstrap', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000;",
                        cancellationToken);
                }

                await EnsureSystemRolesSeededAsync();

                // Check ALL PlatformAdmin accounts (including deactivated ones) so we never provision a duplicate admin.
                var hasPlatformAdmin = await (
                    from userRole in _dbContext.UserRoles.AsNoTracking()
                    join role in _dbContext.Roles.AsNoTracking() on userRole.RoleId equals role.Id
                    where role.NormalizedName == IdentityRoleNames.PlatformAdmin.ToUpperInvariant()
                    select userRole.UserId)
                    .AnyAsync(cancellationToken);

                if (hasPlatformAdmin)
                {
                    await tx.CommitAsync(cancellationToken);
                    return null;
                }

                var adminId = Guid.NewGuid();
                var admin = new RetailFlowIdentityUser
                {
                    Id = adminId,
                    UserName = adminId.ToString("N"),
                    Email = email.Trim(),
                    DisplayName = displayName.Trim(),
                    StoreId = null,
                    IsActive = true,
                    EmailConfirmed = false,
                    CreatedAtUtc = _timeProvider.GetUtcNow()
                };

                var createResult = await _userManager.CreateAsync(admin, password);
                if (!createResult.Succeeded)
                {
                    throw new InvalidOperationException(BuildSafeIdentityError(createResult));
                }

                var roleResult = await _userManager.AddToRoleAsync(admin, IdentityRoleNames.PlatformAdmin);
                if (!roleResult.Succeeded)
                {
                    throw new InvalidOperationException(BuildSafeIdentityError(roleResult));
                }

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
        finally
        {
            if (previousScope is not null)
            {
                _scopeAccessor.SetCurrent(previousScope);
            }
            else
            {
                _scopeAccessor.Clear();
            }
        }
    }

    public async Task<bool> DeactivateUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        user.IsActive = false;
        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return false;
        }

        await _userManager.UpdateSecurityStampAsync(user);
        await _dbContext.SessionTickets
            .Where(t => t.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        return true;
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive)
        {
            return;
        }

        // Token generated via DataProtectorTokenProvider; delivery requires configured IEmailSender in production.
        _ = await _userManager.GeneratePasswordResetTokenAsync(user);
        _logger.LogInformation("Audit: PasswordResetRequested UserId={UserId}", user.Id);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.Email) ||
            string.IsNullOrWhiteSpace(command.Token) ||
            string.IsNullOrWhiteSpace(command.NewPassword) ||
            command.NewPassword.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            return false;
        }

        var user = await _userManager.FindByEmailAsync(command.Email.Trim());
        if (user is null || !user.IsActive)
        {
            return false;
        }

        var result = await _userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);
        if (!result.Succeeded)
        {
            return false;
        }

        await _dbContext.SessionTickets
            .Where(t => t.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        _logger.LogInformation("Audit: PasswordResetCompleted UserId={UserId}", user.Id);
        return true;
    }

    public async Task<TwoFactorSetupDto> BeginTwoFactorSetupAsync(
        Guid userId,
        string issuer,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException("User not found in current scope.");

        var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(unformattedKey))
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
            unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user)
                ?? throw new InvalidOperationException("Unable to generate authenticator key.");
        }

        var formattedKey = FormatKey(unformattedKey);
        var encodedIssuer = UrlEncoder.Default.Encode(issuer);
        var encodedEmail = UrlEncoder.Default.Encode(user.Email ?? user.Id.ToString());
        var authenticatorUri = string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            encodedIssuer,
            encodedEmail,
            unformattedKey);

        return new TwoFactorSetupDto(formattedKey, authenticatorUri);
    }

    public async Task<TwoFactorRecoveryCodesDto?> EnableTwoFactorAsync(
        Guid userId,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(verificationCode))
        {
            return null;
        }

        var sanitizedCode = verificationCode.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            sanitizedCode);

        if (!isValid)
        {
            return null;
        }

        await _userManager.SetTwoFactorEnabledAsync(user, true);
        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return new TwoFactorRecoveryCodesDto(recoveryCodes?.ToArray() ?? []);
    }

    public async Task<bool> DisableTwoFactorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (result.Succeeded)
        {
            await _userManager.ResetAuthenticatorKeyAsync(user);
        }

        return result.Succeeded;
    }

    public async Task<TwoFactorRecoveryCodesDto?> RegenerateRecoveryCodesAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !await _userManager.GetTwoFactorEnabledAsync(user))
        {
            return null;
        }

        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        return new TwoFactorRecoveryCodesDto(codes?.ToArray() ?? []);
    }

    private async Task<AuthSignInResult> VerifyPasswordAndSignInAsync(
        RetailFlowIdentityUser user,
        AppRole role,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var storedHash = user.PasswordHash ?? string.Empty;
        var checkResult = await _signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);

        // If a concurrent login transparently upgraded a legacy hash mid-flight, UserManager's UpdatePasswordHash
        // hits optimistic concurrency (ConcurrencyFailure) and returns Failed. Reload once and retry if PasswordHash changed.
        if (!checkResult.Succeeded && !checkResult.IsLockedOut && !checkResult.IsNotAllowed)
        {
            var latestHash = await _dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == user.Id)
                .Select(u => u.PasswordHash)
                .SingleOrDefaultAsync(cancellationToken);

            if (!string.Equals(latestHash, storedHash, StringComparison.Ordinal))
            {
                var freshUser = await _userManager.FindByIdAsync(user.Id.ToString());
                if (freshUser is not null)
                {
                    user = freshUser;
                    storedHash = user.PasswordHash ?? string.Empty;
                    checkResult = await _signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);
                }
            }
        }

        if (checkResult.IsLockedOut)
        {
            return new AuthSignInResult(AuthSignInOutcome.LockedOut);
        }

        if (checkResult.IsNotAllowed)
        {
            return new AuthSignInResult(AuthSignInOutcome.NotAllowed);
        }

        if (!checkResult.Succeeded)
        {
            return new AuthSignInResult(AuthSignInOutcome.Failed);
        }

        await PersistTransparentRehashIfNeededAsync(user, storedHash, command.Password, cancellationToken);

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            var twoFactorPassed = false;
            if (!string.IsNullOrWhiteSpace(command.TwoFactorCode))
            {
                var sanitized = command.TwoFactorCode.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal);
                twoFactorPassed = await _userManager.VerifyTwoFactorTokenAsync(
                    user,
                    _userManager.Options.Tokens.AuthenticatorTokenProvider,
                    sanitized);
            }
            else if (!string.IsNullOrWhiteSpace(command.RecoveryCode))
            {
                var redeemResult = await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                    user,
                    command.RecoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal));
                twoFactorPassed = redeemResult.Succeeded;
            }

            if (!twoFactorPassed)
            {
                return new AuthSignInResult(AuthSignInOutcome.RequiresTwoFactor);
            }
        }

        if (_httpContextAccessor.HttpContext is not null)
        {
            await _signInManager.SignInAsync(user, isPersistent: true);
        }

        return new AuthSignInResult(
            AuthSignInOutcome.Succeeded,
            new AuthenticatedUserDto(user.Id, user.StoreId, user.Email!, user.DisplayName, role));
    }

    private async Task PersistTransparentRehashIfNeededAsync(
        RetailFlowIdentityUser user,
        string previousHash,
        string password,
        CancellationToken cancellationToken)
    {
        var verifyResult = _passwordHasher.VerifyHashedPassword(user, previousHash, password);
        if (verifyResult != PasswordVerificationResult.SuccessRehashNeeded)
        {
            return;
        }

        try
        {
            var upgradedHash = _passwordHasher.HashPassword(user, password);
            await _dbContext.Users
                .Where(u => u.Id == user.Id && u.PasswordHash == previousHash)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(u => u.PasswordHash, upgradedHash),
                    cancellationToken);

            user.PasswordHash = upgradedHash;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist transparent password rehash for UserId={UserId}.", user.Id);
        }
    }

    private async Task EnsureSystemRolesSeededAsync()
    {
        foreach (var role in new[] { AppRole.Customer, AppRole.MerchantOwner, AppRole.PlatformAdmin })
        {
            var roleName = IdentityRoleNames.FromAppRole(role);
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var identityRole = new RetailFlowIdentityRole
                {
                    Id = RetailFlowIdentityRole.GetSystemRoleId(role),
                    Name = roleName,
                    NormalizedName = roleName.ToUpperInvariant()
                };

                var result = await _roleManager.CreateAsync(identityRole);
                if (!result.Succeeded && !await _roleManager.RoleExistsAsync(roleName))
                {
                    throw new InvalidOperationException(BuildSafeIdentityError(result));
                }
            }
        }
    }

    private void EnsureScopeMatches(IdentityLookupScope expected)
    {
        var current = _scopeAccessor.Current;
        if (current is null || current != expected)
        {
            _scopeAccessor.SetCurrent(expected);
        }
    }

    private static AppRole? ResolveSingleStoreRole(IList<string> roles)
    {
        if (roles.Contains(IdentityRoleNames.PlatformAdmin, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        if (roles.Contains(IdentityRoleNames.MerchantOwner, StringComparer.OrdinalIgnoreCase))
        {
            return AppRole.MerchantOwner;
        }

        if (roles.Contains(IdentityRoleNames.Customer, StringComparer.OrdinalIgnoreCase))
        {
            return AppRole.Customer;
        }

        return null;
    }

    private static string BuildSafeIdentityError(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));

    private static string FormatKey(string unformattedKey)
    {
        var result = new StringBuilder();
        var currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }
}
