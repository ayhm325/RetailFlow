using Microsoft.AspNetCore.Identity;

namespace RetailFlow.IdentitySpike.Api.Domain.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public AccountKind AccountKind { get; set; }

    public Guid? StoreId { get; set; }
}