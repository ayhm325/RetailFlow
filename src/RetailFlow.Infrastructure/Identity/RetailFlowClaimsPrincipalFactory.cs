using System.Security.Claims;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// Generates ClaimsPrincipal with RetailFlow claims: NameIdentifier, Email, Name (= DisplayName), Role (from Identity Roles), and store_id.
/// </summary>
public sealed class RetailFlowClaimsPrincipalFactory
    : UserClaimsPrincipalFactory<RetailFlowIdentityUser, RetailFlowIdentityRole>
{
    public RetailFlowClaimsPrincipalFactory(
        UserManager<RetailFlowIdentityUser> userManager,
        RoleManager<RetailFlowIdentityRole> roleManager,
        IOptions<IdentityOptions> optionsAccessor)
        : base(userManager, roleManager, optionsAccessor)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(RetailFlowIdentityUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);

        // Replace GUID-based UserName claim with human-readable DisplayName while keeping NameIdentifier as Guid Id.
        var existingName = identity.FindFirst(Options.ClaimsIdentity.UserNameClaimType);
        if (existingName is not null)
        {
            identity.RemoveClaim(existingName);
        }

        identity.AddClaim(new Claim(ClaimTypes.Name, user.DisplayName));

        if (!string.IsNullOrWhiteSpace(user.Email) && !identity.HasClaim(c => c.Type == ClaimTypes.Email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, user.Email));
        }

        if (user.StoreId.HasValue)
        {
            identity.AddClaim(new Claim("store_id", user.StoreId.Value.ToString()));
        }

        identity.AddClaim(new Claim("mfa_enrolled", user.TwoFactorEnabled ? "true" : "false"));
        if (user.TwoFactorEnabled)
        {
            identity.AddClaim(new Claim("amr", "mfa"));
        }

        return identity;
    }
}
