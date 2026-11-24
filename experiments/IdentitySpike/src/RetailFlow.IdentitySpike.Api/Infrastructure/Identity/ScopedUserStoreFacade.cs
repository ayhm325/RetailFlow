using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

/// <summary>
/// Exposes only the ASP.NET Core Identity capabilities required by
/// the RetailFlow MVP.
///
/// ScopedUserStore remains responsible for persistence and scope enforcement.
/// This facade deliberately does not expose optional interfaces such as
/// IUserPasskeyStore, IUserLoginStore, or IUserTwoFactorStore.
/// </summary>
public sealed class ScopedUserStoreFacade :
    IUserPasswordStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserClaimStore<ApplicationUser>,
    IQueryableUserStore<ApplicationUser>
{
    private readonly ScopedUserStore _inner;

    public ScopedUserStoreFacade(ScopedUserStore inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public IQueryable<ApplicationUser> Users => _inner.Users;

    // IUserStore

    public Task<string> GetUserIdAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetUserIdAsync(user, cancellationToken);

    public Task<string?> GetUserNameAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetUserNameAsync(user, cancellationToken);

    public Task SetUserNameAsync(
        ApplicationUser user,
        string? userName,
        CancellationToken cancellationToken)
        => _inner.SetUserNameAsync(user, userName, cancellationToken);

    public Task<string?> GetNormalizedUserNameAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetNormalizedUserNameAsync(user, cancellationToken);

    public Task SetNormalizedUserNameAsync(
        ApplicationUser user,
        string? normalizedName,
        CancellationToken cancellationToken)
        => _inner.SetNormalizedUserNameAsync(
            user,
            normalizedName,
            cancellationToken);

    public Task<IdentityResult> CreateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.CreateAsync(user, cancellationToken);

    public Task<IdentityResult> UpdateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.UpdateAsync(user, cancellationToken);

    public Task<IdentityResult> DeleteAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.DeleteAsync(user, cancellationToken);

    public Task<ApplicationUser?> FindByIdAsync(
        string userId,
        CancellationToken cancellationToken)
        => _inner.FindByIdAsync(userId, cancellationToken);

    public Task<ApplicationUser?> FindByNameAsync(
        string normalizedUserName,
        CancellationToken cancellationToken)
        => _inner.FindByNameAsync(normalizedUserName, cancellationToken);

    // IUserPasswordStore

    public Task SetPasswordHashAsync(
        ApplicationUser user,
        string? passwordHash,
        CancellationToken cancellationToken)
        => _inner.SetPasswordHashAsync(
            user,
            passwordHash,
            cancellationToken);

    public Task<string?> GetPasswordHashAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetPasswordHashAsync(user, cancellationToken);

    public Task<bool> HasPasswordAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.HasPasswordAsync(user, cancellationToken);

    // IUserEmailStore

    public Task SetEmailAsync(
        ApplicationUser user,
        string? email,
        CancellationToken cancellationToken)
        => _inner.SetEmailAsync(user, email, cancellationToken);

    public Task<string?> GetEmailAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetEmailAsync(user, cancellationToken);

    public Task<bool> GetEmailConfirmedAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetEmailConfirmedAsync(user, cancellationToken);

    public Task SetEmailConfirmedAsync(
        ApplicationUser user,
        bool confirmed,
        CancellationToken cancellationToken)
        => _inner.SetEmailConfirmedAsync(
            user,
            confirmed,
            cancellationToken);

    public Task<ApplicationUser?> FindByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
        => _inner.FindByEmailAsync(normalizedEmail, cancellationToken);

    public Task<string?> GetNormalizedEmailAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetNormalizedEmailAsync(user, cancellationToken);

    public Task SetNormalizedEmailAsync(
        ApplicationUser user,
        string? normalizedEmail,
        CancellationToken cancellationToken)
        => _inner.SetNormalizedEmailAsync(
            user,
            normalizedEmail,
            cancellationToken);

    // IUserSecurityStampStore

    public Task SetSecurityStampAsync(
        ApplicationUser user,
        string stamp,
        CancellationToken cancellationToken)
        => _inner.SetSecurityStampAsync(user, stamp, cancellationToken);

    public Task<string?> GetSecurityStampAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetSecurityStampAsync(user, cancellationToken);

    // IUserLockoutStore

    public Task<DateTimeOffset?> GetLockoutEndDateAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetLockoutEndDateAsync(user, cancellationToken);

    public Task SetLockoutEndDateAsync(
        ApplicationUser user,
        DateTimeOffset? lockoutEnd,
        CancellationToken cancellationToken)
        => _inner.SetLockoutEndDateAsync(
            user,
            lockoutEnd,
            cancellationToken);

    public Task<int> IncrementAccessFailedCountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.IncrementAccessFailedCountAsync(user, cancellationToken);

    public Task ResetAccessFailedCountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.ResetAccessFailedCountAsync(user, cancellationToken);

    public Task<int> GetAccessFailedCountAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetAccessFailedCountAsync(user, cancellationToken);

    public Task<bool> GetLockoutEnabledAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetLockoutEnabledAsync(user, cancellationToken);

    public Task SetLockoutEnabledAsync(
        ApplicationUser user,
        bool enabled,
        CancellationToken cancellationToken)
        => _inner.SetLockoutEnabledAsync(user, enabled, cancellationToken);

    // IUserClaimStore

    public Task<IList<Claim>> GetClaimsAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
        => _inner.GetClaimsAsync(user, cancellationToken);

    public Task AddClaimsAsync(
        ApplicationUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken)
        => _inner.AddClaimsAsync(user, claims, cancellationToken);

    public Task ReplaceClaimAsync(
        ApplicationUser user,
        Claim claim,
        Claim newClaim,
        CancellationToken cancellationToken)
        => _inner.ReplaceClaimAsync(
            user,
            claim,
            newClaim,
            cancellationToken);

    public Task RemoveClaimsAsync(
        ApplicationUser user,
        IEnumerable<Claim> claims,
        CancellationToken cancellationToken)
        => _inner.RemoveClaimsAsync(user, claims, cancellationToken);

    public Task<IList<ApplicationUser>> GetUsersForClaimAsync(
        Claim claim,
        CancellationToken cancellationToken)
        => _inner.GetUsersForClaimAsync(claim, cancellationToken);

    /// <summary>
    /// The scoped DI container owns and disposes ScopedUserStore.
    /// UserManager may dispose this facade independently, so it must not
    /// dispose the shared inner dependency.
    /// </summary>
    public void Dispose()
    {
    }
}
