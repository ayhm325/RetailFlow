using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// Enforces RetailFlow's central password policy aligned with NIST SP 800-63B-4:
/// - No arbitrary character-class composition rules (spaces and Unicode passphrases are permitted).
/// - Minimum length (15 via Identity PasswordOptions) and bounded maximum length (1024).
/// - Whole-password blocklist against common/breached passwords, product terms, account identifiers, and trivial patterns.
/// </summary>
public sealed class RetailFlowPasswordValidator : IPasswordValidator<RetailFlowIdentityUser>
{
    public const int MinPasswordLength = 15;

    private static readonly HashSet<string> CommonAndProductBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "passwordpassword",
        "password1234567",
        "password12345678",
        "123456789012345",
        "1234567890123456",
        "qwertyuiopasdfg",
        "qwertyuiopasdfgh",
        "qwerty123456789",
        "adminadminadmin",
        "administrator123",
        "welcome12345678",
        "letmeinletmein1",
        "iloveyouiloveyou",
        "changeme1234567",
        "correcthorsebatterystaple",
        "retailflow12345",
        "retailflowadmin",
        "retailflowplatform",
        "retailflowstore"
    };

    private readonly ILogger<RetailFlowPasswordValidator> _logger;

    public RetailFlowPasswordValidator(ILogger<RetailFlowPasswordValidator> logger)
    {
        _logger = logger;
    }

    public Task<IdentityResult> ValidateAsync(
        UserManager<RetailFlowIdentityUser> manager,
        RetailFlowIdentityUser user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(Reject(user.Id, "PasswordEmpty", "Password cannot be empty or whitespace only."));
        }

        var normalized = password.IsNormalized(NormalizationForm.FormC)
            ? password
            : password.Normalize(NormalizationForm.FormC);

        if (normalized.Length < MinPasswordLength)
        {
            return Task.FromResult(Reject(
                user.Id,
                "PasswordTooShort",
                $"Password must be at least {MinPasswordLength} characters."));
        }

        if (normalized.Length > RetailFlowPasswordHasher.MaxPasswordLength)
        {
            return Task.FromResult(Reject(
                user.Id,
                "PasswordTooLong",
                $"Password must not exceed {RetailFlowPasswordHasher.MaxPasswordLength} characters."));
        }

        if (CommonAndProductBlocklist.Contains(normalized))
        {
            return Task.FromResult(Reject(
                user.Id,
                "PasswordTooCommon",
                "Password is too common or known to be compromised. Choose a stronger passphrase."));
        }

        if (MatchesAccountIdentifier(normalized, user))
        {
            return Task.FromResult(Reject(
                user.Id,
                "PasswordMatchesAccountInfo",
                "Password must not match your email address, display name, or username."));
        }

        if (IsSingleRepeatedCharacter(normalized) || IsSimpleSequentialPattern(normalized))
        {
            return Task.FromResult(Reject(
                user.Id,
                "PasswordTooPredictable",
                "Password is too predictable. Avoid repeated or sequential characters."));
        }

        return Task.FromResult(IdentityResult.Success);
    }

    private IdentityResult Reject(Guid userId, string code, string description)
    {
        _logger.LogWarning("Audit: PasswordRejectedByPolicy UserId={UserId} Code={Code}", userId, code);
        return IdentityResult.Failed(new IdentityError
        {
            Code = code,
            Description = description
        });
    }

    private static bool MatchesAccountIdentifier(string password, RetailFlowIdentityUser user)
    {
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            var email = user.Email.Trim();
            if (string.Equals(password, email, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var atIdx = email.IndexOf('@');
            if (atIdx > 0)
            {
                var localPart = email[..atIdx];
                if (localPart.Length >= 4 && string.Equals(password, localPart, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName) &&
            user.DisplayName.Trim().Length >= 4 &&
            string.Equals(password, user.DisplayName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(user.UserName) &&
            user.UserName.Trim().Length >= 4 &&
            string.Equals(password, user.UserName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool IsSingleRepeatedCharacter(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var first = value[0];
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != first)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSimpleSequentialPattern(string value)
    {
        if (value.Length < 8)
        {
            return false;
        }

        var ascending = true;
        var descending = true;
        for (var i = 1; i < value.Length; i++)
        {
            var diff = value[i] - value[i - 1];
            if (diff != 1) ascending = false;
            if (diff != -1) descending = false;
            if (!ascending && !descending) return false;
        }

        return true;
    }
}
