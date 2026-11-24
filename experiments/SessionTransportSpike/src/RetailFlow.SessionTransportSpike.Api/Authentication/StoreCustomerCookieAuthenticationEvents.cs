using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

/// <summary>
/// Validates that an authenticated StoreCustomer ticket
/// belongs to the IdentityLookupScope of the current request.
///
/// Security Stamp validation remains delegated to ASP.NET Core Identity.
/// </summary>
public sealed class StoreCustomerCookieAuthenticationEvents
    : CookieAuthenticationEvents
{
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public StoreCustomerCookieAuthenticationEvents(
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        _scopeAccessor = scopeAccessor;
    }

    public override async Task ValidatePrincipal(
        CookieValidatePrincipalContext context)
    {
        var scope = _scopeAccessor.Current;
        var principal = context.Principal;

        var accountKindClaims = principal?
            .FindAll(RetailFlowClaimTypes.AccountKind)
            .Select(claim => claim.Value)
            .ToArray();

        var storeIdClaims = principal?
            .FindAll(RetailFlowClaimTypes.StoreId)
            .Select(claim => claim.Value)
            .ToArray();

        var validScope =
            scope is not null &&
            scope.AccountKind == AccountKind.StoreCustomer &&
            scope.StoreId is Guid currentStoreId &&
            currentStoreId != Guid.Empty;

        var validAccountKind =
            accountKindClaims is { Length: 1 } &&
            accountKindClaims[0] == AccountKind.StoreCustomer.ToString();

        Guid claimedStoreId = Guid.Empty;

        var validStoreClaim =
            storeIdClaims is { Length: 1 } &&
            Guid.TryParseExact(
                storeIdClaims[0],
                "D",
                out claimedStoreId);

        if (!validScope ||
            !validAccountKind ||
            !validStoreClaim ||
            claimedStoreId != scope!.StoreId!.Value)
        {
            context.RejectPrincipal();

            // Delete the cookie selected for the current request scope.
            await context.HttpContext.SignOutAsync(
                IdentityConstants.ApplicationScheme);

            return;
        }

        // Use the official ASP.NET Core Identity Security Stamp validator.
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    public override Task RedirectToLogin(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
