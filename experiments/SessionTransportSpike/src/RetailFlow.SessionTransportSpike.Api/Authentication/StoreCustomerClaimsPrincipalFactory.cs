using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

/// <summary>
/// Creates the standard ASP.NET Core Identity principal,
/// then adds server-owned RetailFlow account scope claims.
/// </summary>
public sealed class StoreCustomerClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<ApplicationUser>
{
    public StoreCustomerClaimsPrincipalFactory(
        UserManager<ApplicationUser> userManager,
        IOptions<IdentityOptions> options)
        : base(userManager, options)
    {
    }

    public override async Task<ClaimsPrincipal> CreateAsync(
        ApplicationUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var principal = await base.CreateAsync(user);

        var identity = principal.Identities.SingleOrDefault();

        if (identity is null)
        {
            throw new InvalidOperationException(
                "Identity did not create exactly one ClaimsIdentity.");
        }

        // These claim types are reserved for server-generated values.
        foreach (var claim in identity
                     .FindAll(RetailFlowClaimTypes.AccountKind)
                     .ToArray())
        {
            identity.RemoveClaim(claim);
        }

        foreach (var claim in identity
                     .FindAll(RetailFlowClaimTypes.StoreId)
                     .ToArray())
        {
            identity.RemoveClaim(claim);
        }

        identity.AddClaim(
            new Claim(
                RetailFlowClaimTypes.AccountKind,
                user.AccountKind.ToString()));

        if (user.AccountKind == AccountKind.StoreCustomer)
        {
            if (user.StoreId is not Guid storeId ||
                storeId == Guid.Empty)
            {
                throw new InvalidOperationException(
                    "StoreCustomer account must have a valid StoreId.");
            }

            identity.AddClaim(
                new Claim(
                    RetailFlowClaimTypes.StoreId,
                    storeId.ToString("D")));
        }
        else if (user.StoreId is not null)
        {
            throw new InvalidOperationException(
                "BackOffice account cannot have a StoreId.");
        }

        return principal;
    }
}
