using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.Auth;

/// <summary>
/// Validates Identity SecurityStamp and RetailFlow-specific IsActive/StoreId invariants on scoped requests.
/// Public routes without an IdentityLookupScope skip tenant-scoped UserStore lookups while preserving the SessionTicket;
/// protected routes without an established scope fail closed.
/// </summary>
public sealed class RetailFlowCookieEvents : CookieAuthenticationEvents
{
    private readonly UserManager<RetailFlowIdentityUser> _userManager;
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;
    private readonly IOptions<PlatformSecurityOptions> _platformSecurityOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RetailFlowCookieEvents> _logger;

    public RetailFlowCookieEvents(
        UserManager<RetailFlowIdentityUser> userManager,
        IIdentityLookupScopeAccessor scopeAccessor,
        IOptions<PlatformSecurityOptions> platformSecurityOptions,
        TimeProvider timeProvider,
        ILogger<RetailFlowCookieEvents> logger)
    {
        _userManager = userManager;
        _scopeAccessor = scopeAccessor;
        _platformSecurityOptions = platformSecurityOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var parsedUserId))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return;
        }

        var now = _timeProvider.GetUtcNow();
        if (context.Properties.Items.TryGetValue(SqlSessionTicketStore.AbsoluteExpirationPropertyKey, out var absRaw) &&
            DateTimeOffset.TryParse(absRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var absExpiresAt))
        {
            if (now >= absExpiresAt)
            {
                _logger.LogInformation("Audit: SessionAbsoluteTimeoutReached UserId={UserId}", parsedUserId);
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                return;
            }

            if (context.Properties.ExpiresUtc.HasValue && context.Properties.ExpiresUtc.Value > absExpiresAt)
            {
                context.Properties.ExpiresUtc = absExpiresAt;
            }
        }

        var currentScope = _scopeAccessor.Current;
        if (currentScope is null)
        {
            var requiresAuthorization = context.HttpContext.GetEndpoint()?.Metadata.GetMetadata<IAuthorizeData>() is not null;
            if (requiresAuthorization)
            {
                // Fail closed: never allow a protected endpoint without an established tenant/platform scope.
                context.RejectPrincipal();
            }

            // General public route (/health/*, static assets, SPA fallback): skip unscoped UserStore lookup
            // without destroying the user's SessionTicket.
            return;
        }

        var storeIdClaim = context.Principal!.FindFirstValue("store_id");
        var hasStoreClaim = Guid.TryParse(storeIdClaim, out var claimStoreId);

        // If a valid Store A cookie is sent to a Store B route, do not destroy Store A's SessionTicket;
        // let Authorization / TenantClaimMatchEndpointFilter return 403 Forbidden.
        if (currentScope.ScopeType == IdentityScopeType.Store &&
            (!hasStoreClaim || claimStoreId != currentScope.StoreId))
        {
            return;
        }

        if (currentScope.ScopeType == IdentityScopeType.Platform && hasStoreClaim)
        {
            return;
        }

        var originalPrincipalHadMfa =
            string.Equals(context.Principal!.FindFirstValue("amr"), "mfa", StringComparison.Ordinal) ||
            string.Equals(context.Principal!.FindFirstValue("mfa_enrolled"), "true", StringComparison.Ordinal);

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
            return;
        }

        if (_platformSecurityOptions.Value.RequireAdminMfa &&
            context.Principal.IsInRole(nameof(AppRole.PlatformAdmin)) &&
            originalPrincipalHadMfa &&
            (!user.TwoFactorEnabled ||
             user.MfaEnrollmentStatus is not (MfaEnrollmentStatus.Enabled or MfaEnrollmentStatus.ReplacementPending)))
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
