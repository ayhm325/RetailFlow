using System.Data;
using System.Globalization;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using RetailFlow.Application.Identity;
using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Identity;

public sealed class AuthService : IAuthService
{
    private const int MaxMfaFailedAttempts = 5;
    private static readonly TimeSpan MfaLockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan PendingMfaSetupLifetime = TimeSpan.FromMinutes(15);
    private const string InternalLoginProvider = "[AspNetUserStore]";
    private const string AuthenticatorKeyTokenName = "AuthenticatorKey";

    private readonly RetailFlowDbContext _dbContext;
    private readonly UserManager<RetailFlowIdentityUser> _userManager;
    private readonly RoleManager<RetailFlowIdentityRole> _roleManager;
    private readonly SignInManager<RetailFlowIdentityUser> _signInManager;
    private readonly IPasswordHasher<RetailFlowIdentityUser> _passwordHasher;
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly IRetailFlowEmailSender _emailSender;
    private readonly EmailSettings _emailSettings;
    private readonly PlatformSecurityOptions _platformSecurityOptions;
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
        IDataProtectionProvider dataProtectionProvider,
        IRetailFlowEmailSender emailSender,
        IOptions<EmailSettings> emailSettings,
        IOptions<PlatformSecurityOptions> platformSecurityOptions,
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
        _dataProtectionProvider = dataProtectionProvider;
        _emailSender = emailSender;
        _emailSettings = emailSettings.Value;
        _platformSecurityOptions = platformSecurityOptions.Value;
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
            CreatedAtUtc = _timeProvider.GetUtcNow(),
            MfaEnrollmentStatus = MfaEnrollmentStatus.NotEnrolled
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
                CreatedAtUtc = _timeProvider.GetUtcNow(),
                MfaEnrollmentStatus = MfaEnrollmentStatus.NotEnrolled
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
                    CreatedAtUtc = _timeProvider.GetUtcNow(),
                    MfaEnrollmentStatus = MfaEnrollmentStatus.NotEnrolled
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

    public async Task RequestPasswordResetAsync(
        string email,
        string? storeSlug,
        CancellationToken cancellationToken)
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

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var encodedEmail = UrlEncoder.Default.Encode(user.Email!);
        var encodedToken = UrlEncoder.Default.Encode(token);
        var baseUrl = string.IsNullOrWhiteSpace(_emailSettings.PublicBaseUrl)
            ? "https://localhost"
            : _emailSettings.PublicBaseUrl.TrimEnd('/');

        var resetPath = string.IsNullOrWhiteSpace(storeSlug)
            ? "/platform/reset-password"
            : $"/stores/{UrlEncoder.Default.Encode(storeSlug)}/reset-password";

        var resetLink = $"{baseUrl}{resetPath}?email={encodedEmail}&token={encodedToken}";
        await _emailSender.SendPasswordResetAsync(user.Email!, resetLink, cancellationToken);
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

        await _emailSender.SendSecurityNotificationAsync(
            user.Email!,
            "RetailFlow Password Changed",
            "Your RetailFlow account password was recently reset. All active sessions have been revoked.",
            cancellationToken);

        _logger.LogInformation("Audit: PasswordResetCompleted UserId={UserId}", user.Id);
        return true;
    }

    public async Task RequestEmailConfirmationAsync(
        string email,
        string? storeSlug,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var user = await _userManager.FindByEmailAsync(email.Trim());
        if (user is null || !user.IsActive || user.EmailConfirmed)
        {
            return;
        }

        var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
        var encodedUserId = UrlEncoder.Default.Encode(user.Id.ToString());
        var encodedToken = UrlEncoder.Default.Encode(token);
        var baseUrl = string.IsNullOrWhiteSpace(_emailSettings.PublicBaseUrl)
            ? "https://localhost"
            : _emailSettings.PublicBaseUrl.TrimEnd('/');

        var confirmPath = string.IsNullOrWhiteSpace(storeSlug)
            ? "/platform/confirm-email"
            : $"/stores/{UrlEncoder.Default.Encode(storeSlug)}/confirm-email";

        var confirmationLink = $"{baseUrl}{confirmPath}?userId={encodedUserId}&token={encodedToken}";
        await _emailSender.SendEmailConfirmationAsync(user.Email!, confirmationLink, cancellationToken);
        _logger.LogInformation("Audit: EmailConfirmationRequested UserId={UserId}", user.Id);
    }

    public async Task<bool> ConfirmEmailAsync(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty || string.IsNullOrWhiteSpace(command.Token))
        {
            return false;
        }

        var user = await _userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            return false;
        }

        var result = await _userManager.ConfirmEmailAsync(user, command.Token);
        if (result.Succeeded)
        {
            _logger.LogInformation("Audit: EmailConfirmed UserId={UserId}", user.Id);
        }

        return result.Succeeded;
    }

    public async Task<TwoFactorSetupDto?> BeginTwoFactorSetupAsync(
        Guid userId,
        string issuer,
        BeginTwoFactorSetupCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            return null;
        }

        // Enrolled accounts must never receive their existing secret or re-enter initial setup.
        if (user.TwoFactorEnabled ||
            user.MfaEnrollmentStatus is MfaEnrollmentStatus.Enabled or MfaEnrollmentStatus.ReplacementPending)
        {
            return null;
        }

        if (string.IsNullOrEmpty(command.CurrentPassword) ||
            !await _userManager.CheckPasswordAsync(user, command.CurrentPassword))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var rawSecret = TotpCryptoHelper.GenerateBase32Secret();
        var expiresAt = now.Add(PendingMfaSetupLifetime);

        user.ProtectedPendingAuthenticatorKey = TotpCryptoHelper.ProtectSecret(_dataProtectionProvider, rawSecret);
        user.MfaEnrollmentStatus = MfaEnrollmentStatus.EnrollmentPending;
        user.MfaPendingExpiresAtUtc = expiresAt;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return null;
        }

        _logger.LogInformation("Audit: MfaSetupInitiated UserId={UserId}", user.Id);
        return BuildSetupDto(rawSecret, issuer, user.Email ?? user.Id.ToString(), expiresAt);
    }

    public async Task<TwoFactorRecoveryCodesDto?> EnableTwoFactorAsync(
        Guid userId,
        EnableTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null ||
            !user.IsActive ||
            user.TwoFactorEnabled ||
            user.MfaEnrollmentStatus != MfaEnrollmentStatus.EnrollmentPending ||
            string.IsNullOrWhiteSpace(user.ProtectedPendingAuthenticatorKey))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (!user.MfaPendingExpiresAtUtc.HasValue || user.MfaPendingExpiresAtUtc.Value <= now)
        {
            user.MfaEnrollmentStatus = MfaEnrollmentStatus.NotEnrolled;
            user.ProtectedPendingAuthenticatorKey = null;
            user.MfaPendingExpiresAtUtc = null;
            await _userManager.UpdateAsync(user);
            return null;
        }

        if (user.MfaLockoutEndUtc.HasValue && user.MfaLockoutEndUtc.Value > now)
        {
            return null;
        }

        var rawSecret = TotpCryptoHelper.UnprotectSecret(_dataProtectionProvider, user.ProtectedPendingAuthenticatorKey);
        if (!await VerifyTotpAgainstSecretWithReplayAndLockoutAsync(user, rawSecret, command.Code, now, cancellationToken))
        {
            return null;
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var tokenResult = await _userManager.SetAuthenticationTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, rawSecret);
        if (!tokenResult.Succeeded)
        {
            return null;
        }

        var enableResult = await _userManager.SetTwoFactorEnabledAsync(user, true);
        if (!enableResult.Succeeded)
        {
            return null;
        }

        user.MfaEnrollmentStatus = MfaEnrollmentStatus.Enabled;
        user.ProtectedPendingAuthenticatorKey = null;
        user.MfaPendingExpiresAtUtc = null;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return null;
        }

        var stampResult = await _userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            return null;
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        if (recoveryCodes is null)
        {
            return null;
        }

        await tx.CommitAsync(cancellationToken);

        if (_httpContextAccessor.HttpContext is not null)
        {
            await _signInManager.RefreshSignInAsync(user);
        }

        _logger.LogInformation("Audit: MfaEnabled UserId={UserId}", user.Id);
        return new TwoFactorRecoveryCodesDto([.. recoveryCodes]);
    }

    public async Task<TwoFactorSetupDto?> BeginTwoFactorReplacementAsync(
        Guid userId,
        string issuer,
        BeginTwoFactorReplacementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || !user.TwoFactorEnabled)
        {
            return null;
        }

        if (!await VerifyStepUpCredentialsAsync(user, command.CurrentPassword, command.CurrentTwoFactorCode, command.RecoveryCode, cancellationToken))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var rawSecret = TotpCryptoHelper.GenerateBase32Secret();
        var expiresAt = now.Add(PendingMfaSetupLifetime);

        user.ProtectedPendingAuthenticatorKey = TotpCryptoHelper.ProtectSecret(_dataProtectionProvider, rawSecret);
        user.MfaEnrollmentStatus = MfaEnrollmentStatus.ReplacementPending;
        user.MfaPendingExpiresAtUtc = expiresAt;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return null;
        }

        _logger.LogInformation("Audit: MfaReplacementInitiated UserId={UserId}", user.Id);
        return BuildSetupDto(rawSecret, issuer, user.Email ?? user.Id.ToString(), expiresAt);
    }

    public async Task<TwoFactorRecoveryCodesDto?> ConfirmTwoFactorReplacementAsync(
        Guid userId,
        ConfirmTwoFactorReplacementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null ||
            !user.IsActive ||
            !user.TwoFactorEnabled ||
            user.MfaEnrollmentStatus != MfaEnrollmentStatus.ReplacementPending ||
            string.IsNullOrWhiteSpace(user.ProtectedPendingAuthenticatorKey))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (!user.MfaPendingExpiresAtUtc.HasValue || user.MfaPendingExpiresAtUtc.Value <= now)
        {
            user.MfaEnrollmentStatus = MfaEnrollmentStatus.Enabled;
            user.ProtectedPendingAuthenticatorKey = null;
            user.MfaPendingExpiresAtUtc = null;
            await _userManager.UpdateAsync(user);
            return null;
        }

        var rawSecret = TotpCryptoHelper.UnprotectSecret(_dataProtectionProvider, user.ProtectedPendingAuthenticatorKey);
        if (!TotpCryptoHelper.VerifyCodeWithTimeStep(rawSecret, command.NewCode, now, out var newStep))
        {
            await RecordMfaFailureAsync(user, now, cancellationToken);
            return null;
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var tokenResult = await _userManager.SetAuthenticationTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName, rawSecret);
        if (!tokenResult.Succeeded)
        {
            return null;
        }

        user.MfaEnrollmentStatus = MfaEnrollmentStatus.Enabled;
        user.ProtectedPendingAuthenticatorKey = null;
        user.MfaPendingExpiresAtUtc = null;
        user.LastUsedTotpTimeStep = newStep;
        user.MfaFailedAttempts = 0;
        user.MfaLockoutEndUtc = null;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return null;
        }

        var stampResult = await _userManager.UpdateSecurityStampAsync(user);
        if (!stampResult.Succeeded)
        {
            return null;
        }

        var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        if (recoveryCodes is null)
        {
            return null;
        }

        await _dbContext.SessionTickets
            .Where(t => t.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        if (_httpContextAccessor.HttpContext is not null)
        {
            await _signInManager.SignOutAsync();
        }

        _logger.LogInformation("Audit: MfaReplaced UserId={UserId}", user.Id);
        return new TwoFactorRecoveryCodesDto([.. recoveryCodes]);
    }

    public async Task<bool> DisableTwoFactorAsync(
        Guid userId,
        DisableTwoFactorCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || !user.TwoFactorEnabled)
        {
            return false;
        }

        if (!await VerifyStepUpCredentialsAsync(user, command.CurrentPassword, command.TwoFactorCode, command.RecoveryCode, cancellationToken))
        {
            return false;
        }

        await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        var disableResult = await _userManager.SetTwoFactorEnabledAsync(user, false);
        if (!disableResult.Succeeded)
        {
            return false;
        }

        await _userManager.RemoveAuthenticationTokenAsync(user, InternalLoginProvider, AuthenticatorKeyTokenName);

        user.MfaEnrollmentStatus = MfaEnrollmentStatus.NotEnrolled;
        user.ProtectedPendingAuthenticatorKey = null;
        user.MfaPendingExpiresAtUtc = null;
        user.LastUsedTotpTimeStep = null;
        user.MfaFailedAttempts = 0;
        user.MfaLockoutEndUtc = null;

        var updateResult = await _userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return false;
        }

        await _userManager.UpdateSecurityStampAsync(user);
        await _dbContext.SessionTickets
            .Where(t => t.UserId == user.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        if (_httpContextAccessor.HttpContext is not null)
        {
            await _signInManager.SignOutAsync();
        }

        _logger.LogInformation("Audit: MfaDisabled UserId={UserId}", user.Id);
        return true;
    }

    public async Task<TwoFactorRecoveryCodesDto?> RegenerateRecoveryCodesAsync(
        Guid userId,
        RegenerateRecoveryCodesCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive || !user.TwoFactorEnabled)
        {
            return null;
        }

        var activeSecret = await _userManager.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrWhiteSpace(activeSecret))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (!await VerifyTotpAgainstSecretWithReplayAndLockoutAsync(user, activeSecret, command.TwoFactorCode, now, cancellationToken))
        {
            return null;
        }

        await _userManager.UpdateSecurityStampAsync(user);
        var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
        if (codes is null)
        {
            return null;
        }

        _logger.LogInformation("Audit: MfaRecoveryCodesRegenerated UserId={UserId}", user.Id);
        return new TwoFactorRecoveryCodesDto([.. codes]);
    }

    private async Task<AuthSignInResult> VerifyPasswordAndSignInAsync(
        RetailFlowIdentityUser user,
        AppRole role,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var storedHash = user.PasswordHash ?? string.Empty;
        var checkResult = await _signInManager.CheckPasswordSignInAsync(user, command.Password, lockoutOnFailure: true);

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

        var mfaRequiredForPlatform = role == AppRole.PlatformAdmin && _platformSecurityOptions.RequireAdminMfa;

        if (user.TwoFactorEnabled)
        {
            var now = _timeProvider.GetUtcNow();
            if (user.MfaLockoutEndUtc.HasValue && user.MfaLockoutEndUtc.Value > now)
            {
                return new AuthSignInResult(AuthSignInOutcome.LockedOut);
            }

            if (string.IsNullOrWhiteSpace(command.TwoFactorCode) && string.IsNullOrWhiteSpace(command.RecoveryCode))
            {
                return new AuthSignInResult(AuthSignInOutcome.RequiresTwoFactor);
            }

            var twoFactorPassed = false;
            if (!string.IsNullOrWhiteSpace(command.TwoFactorCode))
            {
                var activeSecret = await _userManager.GetAuthenticatorKeyAsync(user);
                if (!string.IsNullOrWhiteSpace(activeSecret))
                {
                    twoFactorPassed = await VerifyTotpAgainstSecretWithReplayAndLockoutAsync(
                        user,
                        activeSecret,
                        command.TwoFactorCode,
                        now,
                        cancellationToken);
                }
            }
            else if (!string.IsNullOrWhiteSpace(command.RecoveryCode))
            {
                var redeemResult = await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                    user,
                    command.RecoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal));
                twoFactorPassed = redeemResult.Succeeded;

                if (!twoFactorPassed)
                {
                    await RecordMfaFailureAsync(user, now, cancellationToken);
                }
            }

            if (!twoFactorPassed)
            {
                var lockedAfterFailure = user.MfaLockoutEndUtc.HasValue && user.MfaLockoutEndUtc.Value > now;
                return new AuthSignInResult(lockedAfterFailure ? AuthSignInOutcome.LockedOut : AuthSignInOutcome.RequiresTwoFactor);
            }
        }
        else if (mfaRequiredForPlatform)
        {
            // PlatformAdmin without MFA receives a restricted enrollment-only session (cannot access /api/platform/stores/*).
            if (_httpContextAccessor.HttpContext is not null)
            {
                await _signInManager.SignInAsync(user, isPersistent: false);
            }

            return new AuthSignInResult(
                AuthSignInOutcome.MfaEnrollmentRequired,
                new AuthenticatedUserDto(user.Id, user.StoreId, user.Email!, user.DisplayName, role, MfaRequiredForPlatformAccess: true));
        }

        if (_httpContextAccessor.HttpContext is not null)
        {
            await _signInManager.SignInAsync(user, isPersistent: true);
        }

        return new AuthSignInResult(
            AuthSignInOutcome.Succeeded,
            new AuthenticatedUserDto(user.Id, user.StoreId, user.Email!, user.DisplayName, role));
    }

    private async Task<bool> VerifyStepUpCredentialsAsync(
        RetailFlowIdentityUser user,
        string? currentPassword,
        string? twoFactorCode,
        string? recoveryCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(currentPassword) ||
            !await _userManager.CheckPasswordAsync(user, currentPassword))
        {
            return false;
        }

        if (!user.TwoFactorEnabled)
        {
            return true;
        }

        var now = _timeProvider.GetUtcNow();
        if (user.MfaLockoutEndUtc.HasValue && user.MfaLockoutEndUtc.Value > now)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(twoFactorCode))
        {
            var activeSecret = await _userManager.GetAuthenticatorKeyAsync(user);
            if (!string.IsNullOrWhiteSpace(activeSecret) &&
                await VerifyTotpAgainstSecretWithReplayAndLockoutAsync(user, activeSecret, twoFactorCode, now, cancellationToken))
            {
                return true;
            }

            return false;
        }

        if (!string.IsNullOrWhiteSpace(recoveryCode))
        {
            var redeem = await _userManager.RedeemTwoFactorRecoveryCodeAsync(
                user,
                recoveryCode.Replace(" ", string.Empty, StringComparison.Ordinal));
            if (redeem.Succeeded)
            {
                return true;
            }

            await RecordMfaFailureAsync(user, now, cancellationToken);
        }

        return false;
    }

    private async Task<bool> VerifyTotpAgainstSecretWithReplayAndLockoutAsync(
        RetailFlowIdentityUser user,
        string base32Secret,
        string? providedCode,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (user.MfaLockoutEndUtc.HasValue && user.MfaLockoutEndUtc.Value > now)
        {
            return false;
        }

        if (!TotpCryptoHelper.VerifyCodeWithTimeStep(base32Secret, providedCode, now, out var matchedTimeStep))
        {
            await RecordMfaFailureAsync(user, now, cancellationToken);
            return false;
        }

        if (user.LastUsedTotpTimeStep.HasValue && matchedTimeStep <= user.LastUsedTotpTimeStep.Value)
        {
            _logger.LogWarning("Audit: TotpReplayRejected UserId={UserId}", user.Id);
            return false;
        }

        // Atomically claim the time-step and reset MFA failure state to prevent concurrent TOTP replay.
        var affected = await _dbContext.Users
            .Where(u => u.Id == user.Id &&
                        (u.LastUsedTotpTimeStep == null || u.LastUsedTotpTimeStep < matchedTimeStep))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.LastUsedTotpTimeStep, matchedTimeStep)
                    .SetProperty(u => u.MfaFailedAttempts, 0)
                    .SetProperty(u => u.MfaLockoutEndUtc, (DateTimeOffset?)null),
                cancellationToken);

        if (affected == 0)
        {
            _logger.LogWarning("Audit: ConcurrentTotpReplayRejected UserId={UserId}", user.Id);
            return false;
        }

        user.LastUsedTotpTimeStep = matchedTimeStep;
        user.MfaFailedAttempts = 0;
        user.MfaLockoutEndUtc = null;
        return true;
    }

    private async Task RecordMfaFailureAsync(
        RetailFlowIdentityUser user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var lockoutUntil = now.Add(MfaLockoutDuration);
        await _dbContext.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(u => u.MfaFailedAttempts, u => u.MfaFailedAttempts + 1)
                    .SetProperty(
                        u => u.MfaLockoutEndUtc,
                        u => u.MfaFailedAttempts + 1 >= MaxMfaFailedAttempts ? lockoutUntil : u.MfaLockoutEndUtc),
                cancellationToken);

        var updated = await _dbContext.Users
            .AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => new { u.MfaFailedAttempts, u.MfaLockoutEndUtc })
            .SingleOrDefaultAsync(cancellationToken);

        if (updated is not null)
        {
            user.MfaFailedAttempts = updated.MfaFailedAttempts;
            user.MfaLockoutEndUtc = updated.MfaLockoutEndUtc;
        }

        _logger.LogWarning(
            "Audit: MfaVerificationFailed UserId={UserId} FailedAttempts={FailedAttempts}",
            user.Id,
            user.MfaFailedAttempts);
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

    private static TwoFactorSetupDto BuildSetupDto(
        string unformattedKey,
        string issuer,
        string accountEmail,
        DateTimeOffset expiresAt)
    {
        var formattedKey = TotpCryptoHelper.FormatSharedKey(unformattedKey);
        var encodedIssuer = UrlEncoder.Default.Encode(issuer);
        var encodedEmail = UrlEncoder.Default.Encode(accountEmail);
        var authenticatorUri = string.Format(
            CultureInfo.InvariantCulture,
            "otpauth://totp/{0}:{1}?secret={2}&issuer={0}&digits=6",
            encodedIssuer,
            encodedEmail,
            unformattedKey);

        return new TwoFactorSetupDto(formattedKey, authenticatorUri, expiresAt);
    }

    private static string BuildSafeIdentityError(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
