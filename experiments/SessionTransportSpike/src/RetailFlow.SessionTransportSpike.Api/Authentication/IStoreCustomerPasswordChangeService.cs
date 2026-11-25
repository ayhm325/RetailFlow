using Microsoft.AspNetCore.Identity;

using RetailFlow.IdentitySpike.Api.Domain.Identity;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

public interface IStoreCustomerPasswordChangeService
{
    Task<IdentityResult> ChangePasswordAndRevokeSessionsAsync(
        ApplicationUser user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}
