using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.Auth;

/// <summary>
/// Validates Identity SecurityStamp and RetailFlow-specific IsActive/StoreId invariants on every authenticated cookie request.
/// </summary>
public sealed class RetailFlowCookieEvents : CookieAuthenticationEvents
{
    private readonly UserManager<RetailFlowIdentityUser> _userManager;
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public RetailFlowCookieEvents(
        UserManager<RetailFlowIdentityUser> userManager,
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        _userManager = userManager;
        _scopeAccessor = scopeAccessor;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out _))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        var currentScope = _scopeAccessor.Current;
        var storeIdClaim = context.Principal!.FindFirstValue("store_id");
        var hasStoreClaim = Guid.TryParse(storeIdClaim, out var claimStoreId);

        // If a valid Store A cookie is sent to a Store B route, do not destroy Store A's SessionTicket;
        // let Authorization / TenantClaimMatchEndpointFilter return 403 Forbidden.
        if (currentScope?.ScopeType == IdentityScopeType.Store &&
            (!hasStoreClaim || claimStoreId != currentScope.StoreId))
        {
            return;
        }

        if (currentScope?.ScopeType == IdentityScopeType.Platform && hasStoreClaim)
        {
            return;
        }

        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal is null)
        {
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        // UserManager uses ScopedUserStore, enforcing tenant scope before returning the user.
        var user = await _userManager.FindByIdAsync(userIdClaim);
        if (user is null || !user.IsActive)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        if (hasStoreClaim && user.StoreId != claimStoreId)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
