using Microsoft.AspNetCore.Authorization;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

using RetailFlow.SessionTransportSpike.Api.Authentication;

namespace RetailFlow.SessionTransportSpike.Api.Authorization;

public sealed class StoreCustomerScopeAuthorizationHandler
    : AuthorizationHandler<StoreCustomerScopeRequirement>
{
    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public StoreCustomerScopeAuthorizationHandler(
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        ArgumentNullException.ThrowIfNull(scopeAccessor);

        _scopeAccessor = scopeAccessor;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        StoreCustomerScopeRequirement requirement)
    {
        // Authentication and authorization are separate concerns.
        // Anonymous requests are handled by RequireAuthenticatedUser.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return Task.CompletedTask;
        }

        // The scope must have been established by IdentityScopeMiddleware.
        var scope = _scopeAccessor.Current;

        if (scope is null ||
            scope.AccountKind != AccountKind.StoreCustomer ||
            scope.StoreId is not Guid currentStoreId ||
            currentStoreId == Guid.Empty)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // AccountKind must exist exactly once.
        var accountKindClaims = context.User
            .FindAll(RetailFlowClaimTypes.AccountKind)
            .Select(claim => claim.Value)
            .ToArray();

        if (accountKindClaims.Length != 1 ||
            !string.Equals(
                accountKindClaims[0],
                AccountKind.StoreCustomer.ToString(),
                StringComparison.Ordinal))
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // StoreId must exist exactly once and represent a valid GUID.
        var storeIdClaims = context.User
            .FindAll(RetailFlowClaimTypes.StoreId)
            .Select(claim => claim.Value)
            .ToArray();

        Guid claimedStoreId = Guid.Empty;

        var hasValidStoreClaim =
            storeIdClaims.Length == 1 &&
            Guid.TryParseExact(
                storeIdClaims[0],
                "D",
                out claimedStoreId) &&
            claimedStoreId != Guid.Empty;

        if (!hasValidStoreClaim ||
            claimedStoreId != currentStoreId)
        {
            context.Fail();
            return Task.CompletedTask;
        }

        // All requirements have been satisfied.
        context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
