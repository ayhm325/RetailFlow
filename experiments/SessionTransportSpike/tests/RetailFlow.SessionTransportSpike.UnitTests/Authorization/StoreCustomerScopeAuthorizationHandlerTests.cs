using System.Security.Claims;

using Microsoft.AspNetCore.Authorization;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;

using RetailFlow.SessionTransportSpike.Api.Authentication;
using RetailFlow.SessionTransportSpike.Api.Authorization;

namespace RetailFlow.SessionTransportSpike.UnitTests.Authorization;

public sealed class StoreCustomerScopeAuthorizationHandlerTests
{
    [Fact]
    public async Task Handler_Succeeds_WhenStoreCustomerMatchesCurrentScope()
    {
        var storeId = Guid.NewGuid();

        var context = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            [storeId.ToString("D")],
            storeId);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_Fails_WhenClaimedStoreDoesNotMatchCurrentScope()
    {
        var storeA = Guid.NewGuid();
        var storeB = Guid.NewGuid();

        var context = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            [storeA.ToString("D")],
            storeB);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Handler_Fails_WhenAccountKindIsBackOffice()
    {
        var storeId = Guid.NewGuid();

        var context = await EvaluateAsync(
            AccountKind.BackOffice.ToString(),
            [storeId.ToString("D")],
            storeId);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task Handler_Fails_WhenStoreIdClaimIsMissingOrDuplicated()
    {
        var storeId = Guid.NewGuid();

        var missingClaimContext = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            [],
            storeId);

        Assert.True(missingClaimContext.HasFailed);

        var duplicatedClaimContext = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            [
                storeId.ToString("D"),
                storeId.ToString("D")
            ],
            storeId);

        Assert.True(duplicatedClaimContext.HasFailed);
    }

    [Fact]
    public async Task Handler_Fails_WhenStoreIdClaimIsMalformedOrEmpty()
    {
        var currentStoreId = Guid.NewGuid();

        var malformedContext = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            ["not-a-guid"],
            currentStoreId);

        Assert.True(malformedContext.HasFailed);

        var emptyGuidContext = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            [Guid.Empty.ToString("D")],
            currentStoreId);

        Assert.True(emptyGuidContext.HasFailed);
    }

    [Fact]
    public async Task Handler_Fails_WhenCurrentStoreScopeIsMissing()
    {
        var storeId = Guid.NewGuid();

        var context = await EvaluateAsync(
            AccountKind.StoreCustomer.ToString(),
            [storeId.ToString("D")],
            currentStoreId: null);

        Assert.True(context.HasFailed);
    }

    private static async Task<AuthorizationHandlerContext> EvaluateAsync(
        string accountKind,
        IReadOnlyCollection<string> storeIdClaims,
        Guid? currentStoreId)
    {
        var claims = new List<Claim>
        {
            new(
                RetailFlowClaimTypes.AccountKind,
                accountKind)
        };

        claims.AddRange(
            storeIdClaims.Select(
                value => new Claim(
                    RetailFlowClaimTypes.StoreId,
                    value)));

        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                authenticationType: "Test"));

        var scopeAccessor = new IdentityLookupScopeAccessor();

        if (currentStoreId is Guid storeId)
        {
            scopeAccessor.SetCurrent(
                IdentityLookupScope.ForStoreCustomer(storeId));
        }

        var requirement = new StoreCustomerScopeRequirement();

        var context = new AuthorizationHandlerContext(
            [requirement],
            principal,
            resource: null);

        var handler = new StoreCustomerScopeAuthorizationHandler(
            scopeAccessor);

        await handler.HandleAsync(context);

        return context;
    }
}
