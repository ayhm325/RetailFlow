using System.ComponentModel.DataAnnotations;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// Validates user email format and enforces tenant-scoped email uniqueness (StoreId + NormalizedEmail, or StoreId IS NULL + NormalizedEmail)
/// instead of Identity's default global email uniqueness.
/// </summary>
public sealed class RetailFlowUserValidator : IUserValidator<RetailFlowIdentityUser>
{
    private static readonly EmailAddressAttribute EmailValidator = new();

    public async Task<IdentityResult> ValidateAsync(
        UserManager<RetailFlowIdentityUser> manager,
        RetailFlowIdentityUser user)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        var errors = new List<IdentityError>();
        var describer = manager.ErrorDescriber;

        var email = await manager.GetEmailAsync(user);
        if (string.IsNullOrWhiteSpace(email) || !EmailValidator.IsValid(email))
        {
            errors.Add(describer.InvalidEmail(email));
        }
        else
        {
            var normalizedEmail = manager.NormalizeEmail(email);
            // manager.Users is already scoped by ScopedUserStore to the active StoreId or Platform scope.
            var duplicate = await manager.Users
                .AsNoTracking()
                .AnyAsync(u => u.NormalizedEmail == normalizedEmail && u.Id != user.Id);

            if (duplicate)
            {
                errors.Add(describer.DuplicateEmail(email));
            }
        }

        if (string.IsNullOrWhiteSpace(user.DisplayName) || user.DisplayName.Length > 150)
        {
            errors.Add(new IdentityError
            {
                Code = "InvalidDisplayName",
                Description = "Display name is required and must not exceed 150 characters."
            });
        }

        return errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed([.. errors]);
    }
}
