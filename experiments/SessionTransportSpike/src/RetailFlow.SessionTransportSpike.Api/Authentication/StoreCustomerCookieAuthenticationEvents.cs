using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

public sealed class StoreCustomerCookieAuthenticationEvents
    : CookieAuthenticationEvents
{
    public override async Task ValidatePrincipal(
        CookieValidatePrincipalContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var principal = context.Principal;

        var accountKindClaims = principal?
            .FindAll(RetailFlowClaimTypes.AccountKind)
            .Select(claim => claim.Value)
            .ToArray();

        var storeIdClaims = principal?
            .FindAll(RetailFlowClaimTypes.StoreId)
            .Select(claim => claim.Value)
            .ToArray();

        // The StoreCustomer cookie scheme only accepts
        // structurally valid StoreCustomer identities.
        var validAccountKind =
            accountKindClaims is { Length: 1 } &&
            string.Equals(
                accountKindClaims[0],
                AccountKind.StoreCustomer.ToString(),
                StringComparison.Ordinal);

        Guid claimedStoreId = Guid.Empty;

        var validStoreClaim =
            storeIdClaims is { Length: 1 } &&
            Guid.TryParseExact(
                storeIdClaims[0],
                "D",
                out claimedStoreId) &&
            claimedStoreId != Guid.Empty;

        if (!validAccountKind || !validStoreClaim)
        {
            context.RejectPrincipal();

            // Delete only the cookie selected for the current scope.
            // Do not revoke a potentially valid server-side ticket.
            DeleteCurrentScopeCookie(context);

            return;
        }

        // A structurally valid identity is authenticated here.
        //
        // Whether it belongs to the current StoreId is an
        // authorization decision handled by StoreCustomerScopePolicy.
        await SecurityStampValidator.ValidatePrincipalAsync(context);
    }

    private static void DeleteCurrentScopeCookie(
        CookieValidatePrincipalContext context)
    {
        var cookieOptions =
            context.Options.Cookie.Build(context.HttpContext);

        context.Options.CookieManager.DeleteCookie(
            context.HttpContext,
            context.Options.Cookie.Name!,
            cookieOptions);
    }

    public override Task RedirectToLogin(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(
        RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode =
            StatusCodes.Status403Forbidden;

        return Task.CompletedTask;
    }
}
