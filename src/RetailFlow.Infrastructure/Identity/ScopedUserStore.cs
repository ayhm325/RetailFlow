using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// Fail-closed, tenant-aware ASP.NET Core Identity UserStore.
/// Restricts all user queries, lookups, mutations, and role assignments to the active IdentityLookupScope.
/// </summary>
public sealed class ScopedUserStore
    : UserStore<RetailFlowIdentityUser, RetailFlowIdentityRole, RetailFlowDbContext, Guid>
{
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public ScopedUserStore(
        RetailFlowDbContext context,
        IIdentityLookupScopeAccessor scopeAccessor,
        IdentityErrorDescriber? describer = null)
        : base(context, describer)
    {
        _scopeAccessor = scopeAccessor
            ?? throw new ArgumentNullException(nameof(scopeAccessor));
    }

    public override IQueryable<RetailFlowIdentityUser> Users => CreateScopedUsers();

    public override Task<RetailFlowIdentityUser?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        if (!Guid.TryParse(userId, out var parsedId))
        {
            return Task.FromResult<RetailFlowIdentityUser?>(null);
        }

        return CreateScopedUsers()
            .SingleOrDefaultAsync(u => u.Id == parsedId, cancellationToken);
    }

    public override Task<RetailFlowIdentityUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUserName);

        return CreateScopedUsers()
            .SingleOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);
    }

    public override Task<RetailFlowIdentityUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedEmail);

        return CreateScopedUsers()
            .SingleOrDefaultAsync(u => u.NormalizedEmail == normalizedEmail, cancellationToken);
    }

    protected override Task<RetailFlowIdentityUser?> FindUserAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();

        return CreateScopedUsers()
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);
    }

    public override Task<IdentityResult> CreateAsync(
        RetailFlowIdentityUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.CreateAsync(user, cancellationToken);
    }

    public override async Task<IdentityResult> UpdateAsync(
        RetailFlowIdentityUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        var result = await base.UpdateAsync(user, cancellationToken);
        if (!result.Succeeded)
        {
            Context.Entry(user).State = EntityState.Detached;
        }

        return result;
    }

    public override Task<IdentityResult> DeleteAsync(
        RetailFlowIdentityUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.DeleteAsync(user, cancellationToken);
    }

    public override Task SetPasswordHashAsync(
        RetailFlowIdentityUser user,
        string? passwordHash,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.SetPasswordHashAsync(user, passwordHash, cancellationToken);
    }

    public override Task AddToRoleAsync(
        RetailFlowIdentityUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRoleName);

        var scope = EnsureUserMatchesCurrentScope(user);
        EnsureRoleAllowedInScope(scope, user, normalizedRoleName);
        return base.AddToRoleAsync(user, normalizedRoleName, cancellationToken);
    }

    public override Task RemoveFromRoleAsync(
        RetailFlowIdentityUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.RemoveFromRoleAsync(user, normalizedRoleName, cancellationToken);
    }

    public override Task<bool> IsInRoleAsync(
        RetailFlowIdentityUser user,
        string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.IsInRoleAsync(user, normalizedRoleName, cancellationToken);
    }

    public override Task<IList<string>> GetRolesAsync(
        RetailFlowIdentityUser user,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.GetRolesAsync(user, cancellationToken);
    }

    public override Task AddClaimsAsync(
        RetailFlowIdentityUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.AddClaimsAsync(user, claims, cancellationToken);
    }

    public override Task RemoveClaimsAsync(
        RetailFlowIdentityUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.RemoveClaimsAsync(user, claims, cancellationToken);
    }

    public override Task ReplaceClaimAsync(
        RetailFlowIdentityUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.ReplaceClaimAsync(user, claim, newClaim, cancellationToken);
    }

    public override Task AddLoginAsync(
        RetailFlowIdentityUser user,
        UserLoginInfo login,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.AddLoginAsync(user, login, cancellationToken);
    }

    public override Task RemoveLoginAsync(
        RetailFlowIdentityUser user,
        string loginProvider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.RemoveLoginAsync(user, loginProvider, providerKey, cancellationToken);
    }

    public override Task SetTokenAsync(
        RetailFlowIdentityUser user,
        string loginProvider,
        string name,
        string? value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.SetTokenAsync(user, loginProvider, name, value, cancellationToken);
    }

    public override Task RemoveTokenAsync(
        RetailFlowIdentityUser user,
        string loginProvider,
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        EnsureUserMatchesCurrentScope(user);
        return base.RemoveTokenAsync(user, loginProvider, name, cancellationToken);
    }

    private IQueryable<RetailFlowIdentityUser> CreateScopedUsers()
    {
        var scope = RequireCurrentScope();
        return scope.ScopeType switch
        {
            IdentityScopeType.Store => Context.Users.Where(u => u.StoreId == scope.StoreId),
            IdentityScopeType.Platform => Context.Users.Where(u => u.StoreId == null),
            IdentityScopeType.SystemProvisioning => Context.Users.Where(u => u.StoreId == null),
            _ => throw new InvalidOperationException($"Unsupported identity scope type: {scope.ScopeType}.")
        };
    }

    private IdentityLookupScope RequireCurrentScope()
    {
        return _scopeAccessor.Current
            ?? throw new InvalidOperationException(
                "Identity lookup scope is not established. Unscoped Identity UserStore access is forbidden.");
    }

    private IdentityLookupScope EnsureUserMatchesCurrentScope(RetailFlowIdentityUser user)
    {
        var scope = RequireCurrentScope();
        var matches = scope.ScopeType switch
        {
            IdentityScopeType.Store => user.StoreId.HasValue && user.StoreId == scope.StoreId,
            IdentityScopeType.Platform => user.StoreId is null,
            IdentityScopeType.SystemProvisioning => user.StoreId is null,
            _ => false
        };

        if (!matches)
        {
            throw new InvalidOperationException(
                $"Cross-tenant identity operation rejected. User StoreId={user.StoreId} does not match active scope {scope.ScopeType} (StoreId={scope.StoreId}).");
        }

        return scope;
    }

    private static void EnsureRoleAllowedInScope(
        IdentityLookupScope scope,
        RetailFlowIdentityUser user,
        string normalizedRoleName)
    {
        var normalizedPlatformAdmin = IdentityRoleNames.PlatformAdmin.ToUpperInvariant();
        var normalizedCustomer = IdentityRoleNames.Customer.ToUpperInvariant();
        var normalizedMerchantOwner = IdentityRoleNames.MerchantOwner.ToUpperInvariant();

        if (scope.ScopeType == IdentityScopeType.Store)
        {
            if (normalizedRoleName == normalizedPlatformAdmin)
            {
                throw new InvalidOperationException("PlatformAdmin role cannot be assigned within a Store scope.");
            }

            if (normalizedRoleName != normalizedCustomer && normalizedRoleName != normalizedMerchantOwner)
            {
                throw new InvalidOperationException($"Role '{normalizedRoleName}' is not allowed in Store scope.");
            }
        }
        else
        {
            if (user.StoreId is not null || normalizedRoleName != normalizedPlatformAdmin)
            {
                throw new InvalidOperationException("Only PlatformAdmin role with null StoreId is allowed in Platform scope.");
            }
        }
    }
}
