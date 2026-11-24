using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

/// <summary>
/// Identity user store that restricts user lookup and persisted user operations
/// to the Identity scope established for the current request.
/// </summary>
public sealed class ScopedUserStore
    : UserOnlyStore<ApplicationUser, IdentitySpikeDbContext>
{
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public ScopedUserStore(
        IdentitySpikeDbContext context,
        IIdentityLookupScopeAccessor scopeAccessor,
        IdentityErrorDescriber? describer = null)
        : base(context, describer)
    {
        _scopeAccessor = scopeAccessor
            ?? throw new ArgumentNullException(nameof(scopeAccessor));
    }

    /// <summary>
    /// Exposes only users belonging to the current Identity scope.
    /// </summary>
    public override IQueryable<ApplicationUser> Users
        => CreateScopedUsers();

    public override Task<ApplicationUser?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return CreateScopedUsers()
            .SingleOrDefaultAsync(
                user => user.Id == userId,
                cancellationToken);
    }

    public override Task<ApplicationUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        return CreateScopedUsers()
            .SingleOrDefaultAsync(
                user => user.NormalizedEmail == normalizedEmail,
                cancellationToken);
    }

    public override Task<ApplicationUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);

        return CreateScopedUsers()
            .SingleOrDefaultAsync(
                user => user.NormalizedUserName == normalizedUserName,
                cancellationToken);
    }

    /// <summary>
    /// Used by the base store when resolving users from related Identity records.
    /// Keeping this lookup scoped prevents a related record from resolving a user
    /// belonging to another store.
    /// </summary>
    protected override Task<ApplicationUser?> FindUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        return CreateScopedUsers()
            .SingleOrDefaultAsync(
                user => user.Id == userId,
                cancellationToken);
    }

    // -------------------------------------------------------------------------
    // External logins
    // -------------------------------------------------------------------------

    public override Task<ApplicationUser?> FindByLoginAsync(
    string loginProvider,
    string providerKey,
    CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        // Enforce Fail-Closed even when no matching login exists.
        _ = GetRequiredScope();

        return base.FindByLoginAsync(
            loginProvider,
            providerKey,
            cancellationToken);
    }

    public override async Task AddLoginAsync(
        ApplicationUser user,
        UserLoginInfo login,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(login);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        await base.AddLoginAsync(
            user,
            login,
            cancellationToken);
    }

    public override async Task RemoveLoginAsync(
        ApplicationUser user,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        await base.RemoveLoginAsync(
            user,
            loginProvider,
            providerKey,
            cancellationToken);
    }

    public override async Task<IList<UserLoginInfo>> GetLoginsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        return await base.GetLoginsAsync(
            user,
            cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Authentication tokens
    // -------------------------------------------------------------------------

    protected override async Task<IdentityUserToken<string>?> FindTokenAsync(
        ApplicationUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(loginProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        return await base.FindTokenAsync(
            user,
            loginProvider,
            name,
            cancellationToken);
    }

    protected override async Task AddUserTokenAsync(
        IdentityUserToken<string> token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(token);

        await EnsureUserIdBelongsToCurrentScopeAsync(token.UserId);

        await base.AddUserTokenAsync(token);
    }

    protected override async Task RemoveUserTokenAsync(
        IdentityUserToken<string> token)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(token);

        await EnsureUserIdBelongsToCurrentScopeAsync(token.UserId);

        await base.RemoveUserTokenAsync(token);
    }

    // -------------------------------------------------------------------------
    // Lockout and security stamp
    //
    // Identity calls some of these methods while a new user is still being
    // created (before persistence), as well as on existing users. Hybrid scope
    // validation verifies the in-memory scope and rejects any user Id that is
    // already persisted in another scope.
    // -------------------------------------------------------------------------

    public override async Task<DateTimeOffset?> GetLockoutEndDateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        return await base.GetLockoutEndDateAsync(user, cancellationToken);
    }

    public override async Task SetLockoutEndDateAsync(
        ApplicationUser user,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        await base.SetLockoutEndDateAsync(
            user,
            lockoutEnd,
            cancellationToken);
    }

    public override async Task<int> GetAccessFailedCountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        return await base.GetAccessFailedCountAsync(user, cancellationToken);
    }

    public override async Task<int> IncrementAccessFailedCountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        return await base.IncrementAccessFailedCountAsync(user, cancellationToken);
    }

    public override async Task ResetAccessFailedCountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        await base.ResetAccessFailedCountAsync(user, cancellationToken);
    }

    public override async Task<bool> GetLockoutEnabledAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        return await base.GetLockoutEnabledAsync(user, cancellationToken);
    }

    public override async Task SetLockoutEnabledAsync(
        ApplicationUser user,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        await base.SetLockoutEnabledAsync(user, enabled, cancellationToken);
    }

    public override async Task<string?> GetSecurityStampAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        return await base.GetSecurityStampAsync(user, cancellationToken);
    }

    public override async Task SetSecurityStampAsync(
        ApplicationUser user,
        string stamp,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(stamp);

        await EnsureUserMatchesScopeOrIsNewAsync(user, cancellationToken);

        await base.SetSecurityStampAsync(
            user,
            stamp,
            cancellationToken);
    }

    // -------------------------------------------------------------------------
    // User persistence
    // -------------------------------------------------------------------------

    public override async Task<IdentityResult> CreateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        // A new user is expected not to exist in the database yet.
        // The database CHECK/FK/unique indexes remain the final integrity guard.
        if (!BelongsToScope(user, GetRequiredScope()))
        {
            return ScopeViolationResult();
        }

        return await base.CreateAsync(user, cancellationToken);
    }

    public override async Task<IdentityResult> UpdateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (!await IsPersistedUserInCurrentScopeAsync(
                user,
                cancellationToken))
        {
            return ScopeViolationResult();
        }

        return await base.UpdateAsync(user, cancellationToken);
    }

    public override async Task<IdentityResult> DeleteAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        if (!await IsPersistedUserInCurrentScopeAsync(
                user,
                cancellationToken))
        {
            return ScopeViolationResult();
        }

        return await base.DeleteAsync(user, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Claims
    // -------------------------------------------------------------------------

    public override async Task<IList<Claim>> GetClaimsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        return await base.GetClaimsAsync(user, cancellationToken);
    }

    public override async Task AddClaimsAsync(
        ApplicationUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claims);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        await base.AddClaimsAsync(user, claims, cancellationToken);
    }

    public override async Task ReplaceClaimAsync(
        ApplicationUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(newClaim);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        await base.ReplaceClaimAsync(
            user,
            claim,
            newClaim,
            cancellationToken);
    }

    public override async Task RemoveClaimsAsync(
        ApplicationUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(claims);

        await EnsurePersistedUserIsInCurrentScopeAsync(
            user,
            cancellationToken);

        await base.RemoveClaimsAsync(user, claims, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Scope validation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Validates only the in-memory tenant identity of a user.
    /// This deliberately does not query SQL because Identity calls scalar
    /// property methods while a new user is still being created.
    /// </summary>
    private void EnsureUserMatchesCurrentScope(ApplicationUser user)
    {
        var scope = GetRequiredScope();

        if (!BelongsToScope(user, scope))
        {
            throw new InvalidOperationException(
                "The user does not belong to the current identity scope.");
        }
    }

    /// <summary>
    /// Validates both the in-memory scope and, if the user Id is already
    /// persisted in SQL Server, verifies that the persisted row belongs to
    /// the current scope. Allows unpersisted new users during creation.
    /// </summary>
    private async Task EnsureUserMatchesScopeOrIsNewAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        EnsureUserMatchesCurrentScope(user);

        var persistedUser = await Context.Set<ApplicationUser>()
            .AsNoTracking()
            .Where(existing => existing.Id == user.Id)
            .Select(existing => new
            {
                existing.AccountKind,
                existing.StoreId
            })
            .SingleOrDefaultAsync(cancellationToken);

        // A new user has not been persisted yet.
        if (persistedUser is null)
            return;

        var scope = GetRequiredScope();

        if (persistedUser.AccountKind != scope.AccountKind ||
            persistedUser.StoreId != scope.StoreId)
        {
            throw new InvalidOperationException(
                "The user does not belong to the current identity scope.");
        }
    }

    /// <summary>
    /// Validates both the in-memory scope and the persisted ownership of a user.
    /// Use this before reading or writing user-related rows.
    /// </summary>
    private async Task EnsurePersistedUserIsInCurrentScopeAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        EnsureUserMatchesCurrentScope(user);

        var existsInCurrentScope = await CreateScopedUsers()
            .AsNoTracking()
            .AnyAsync(
                existing => existing.Id == user.Id,
                cancellationToken);

        if (!existsInCurrentScope)
        {
            throw new InvalidOperationException(
                "The user was not found in the current identity scope.");
        }
    }

    private async Task<bool> IsPersistedUserInCurrentScopeAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        EnsureUserMatchesCurrentScope(user);

        return await CreateScopedUsers()
            .AsNoTracking()
            .AnyAsync(
                existing => existing.Id == user.Id,
                cancellationToken);
    }

    private async Task EnsureUserIdBelongsToCurrentScopeAsync(
        string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var existsInCurrentScope = await CreateScopedUsers()
            .AsNoTracking()
            .AnyAsync(user => user.Id == userId);

        if (!existsInCurrentScope)
        {
            throw new InvalidOperationException(
                "The user does not belong to the current identity scope.");
        }
    }

    private IQueryable<ApplicationUser> CreateScopedUsers()
    {
        var scope = GetRequiredScope();

        return Context.Set<ApplicationUser>()
            .Where(user =>
                user.AccountKind == scope.AccountKind &&
                user.StoreId == scope.StoreId);
    }

    private IdentityLookupScope GetRequiredScope()
    {
        return _scopeAccessor.Current
            ?? throw new InvalidOperationException(
                "Identity lookup scope has not been established.");
    }

    private static bool BelongsToScope(
        ApplicationUser user,
        IdentityLookupScope scope)
    {
        return user.AccountKind == scope.AccountKind
            && user.StoreId == scope.StoreId;
    }

    private static IdentityResult ScopeViolationResult()
    {
        return IdentityResult.Failed(
            new IdentityError
            {
                Code = "IdentityScopeViolation",
                Description =
                    "The operation is not permitted in the current identity scope."
            });
    }
}
