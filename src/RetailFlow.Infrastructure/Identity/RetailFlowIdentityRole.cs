using Microsoft.AspNetCore.Identity;
using RetailFlow.Domain.Identity;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity role entity with Guid primary keys and deterministic system role IDs.
/// </summary>
public sealed class RetailFlowIdentityRole : IdentityRole<Guid>
{
    public static readonly Guid CustomerRoleId = Guid.Parse("11111111-1111-1111-1111-111111111101");
    public static readonly Guid MerchantOwnerRoleId = Guid.Parse("11111111-1111-1111-1111-111111111102");
    public static readonly Guid PlatformAdminRoleId = Guid.Parse("11111111-1111-1111-1111-111111111103");

    public static Guid GetSystemRoleId(AppRole role) => role switch
    {
        AppRole.Customer => CustomerRoleId,
        AppRole.MerchantOwner => MerchantOwnerRoleId,
        AppRole.PlatformAdmin => PlatformAdminRoleId,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unsupported role.")
    };
}
