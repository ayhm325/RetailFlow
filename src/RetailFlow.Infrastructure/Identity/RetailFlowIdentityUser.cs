using Microsoft.AspNetCore.Identity;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity user entity for RetailFlow with store-scoped tenancy support.
/// Uses Guid primary keys to preserve existing Orders, CheckoutRequests, and SessionTickets references.
/// </summary>
public sealed class RetailFlowIdentityUser : IdentityUser<Guid>
{
    public Guid? StoreId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
