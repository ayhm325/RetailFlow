using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

public sealed class StoreCustomerCookieAuthenticationEvents
    : CookieAuthenticationEvents
{
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public StoreCustomerCookieAuthenticationEvents(
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        ArgumentNullException.ThrowIfNull(scopeAccessor);

        _scopeAccessor = scopeAccessor;
    }

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

        var scope = _scopeAccessor.Current;

        var matchesCurrentStore =
            scope is not null &&
            scope.AccountKind == AccountKind.StoreCustomer &&
            scope.StoreId is Guid currentStoreId &&
            currentStoreId != Guid.Empty &&
            currentStoreId == claimedStoreId;

        if (!matchesCurrentStore)
        {
            // Do not validate a Store A identity using Store B's scoped UserManager.
            // Keep the ticket authenticated and let Authorization deny this request.
            return;
        }

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
