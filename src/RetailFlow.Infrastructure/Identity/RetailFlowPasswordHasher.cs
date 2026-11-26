using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// ASP.NET Core Identity IPasswordHasher adapter supporting:
/// 1) Official Identity V3 PBKDF2-HMAC-SHA512 format (220,000 iterations per OWASP Cheat Sheet).
/// 2) Legacy RetailFlow v1.{Iterations}.{Salt}.{Hash} PBKDF2-HMAC-SHA256 hashes (310,000..1,000,000 iterations),
///    returning SuccessRehashNeeded on match for transparent upgrade.
/// </summary>
public sealed class RetailFlowPasswordHasher : IPasswordHasher<RetailFlowIdentityUser>
{
    public const int MaxPasswordLength = 1024;
    public const int IdentityIterationCount = 220_000;

    private const int LegacySaltSize = 16;
    private const int LegacyKeySize = 32;
    private const int MinAcceptedLegacyIterations = 310_000;
    private const int MaxAcceptedLegacyIterations = 1_000_000;

    private readonly PasswordHasher<RetailFlowIdentityUser> _identityHasher;

    public RetailFlowPasswordHasher()
    {
        _identityHasher = new PasswordHasher<RetailFlowIdentityUser>(
            Options.Create(new PasswordHasherOptions
            {
                CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
                IterationCount = IdentityIterationCount
            }));
    }

    public string HashPassword(RetailFlowIdentityUser user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (string.IsNullOrEmpty(password))
        {
            throw new ArgumentException($"Password length must be between 1 and {MaxPasswordLength} characters.", nameof(password));
        }

        var normalized = NormalizePassword(password);
        if (normalized.Length > MaxPasswordLength)
        {
            throw new ArgumentException($"Password length must be between 1 and {MaxPasswordLength} characters.", nameof(password));
        }

        return _identityHasher.HashPassword(user, normalized);
    }

    public PasswordVerificationResult VerifyHashedPassword(
        RetailFlowIdentityUser user,
        string hashedPassword,
        string providedPassword)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        var normalized = NormalizePassword(providedPassword);
        if (normalized.Length > MaxPasswordLength)
        {
            return PasswordVerificationResult.Failed;
        }

        if (string.IsNullOrWhiteSpace(hashedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        if (hashedPassword.StartsWith("v1.", StringComparison.Ordinal))
        {
            return VerifyLegacyV1(normalized, hashedPassword);
        }

        try
        {
            return _identityHasher.VerifyHashedPassword(user, hashedPassword, normalized);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }

    private static string NormalizePassword(string password) =>
        password.IsNormalized(NormalizationForm.FormC)
            ? password
            : password.Normalize(NormalizationForm.FormC);

    private static PasswordVerificationResult VerifyLegacyV1(string password, string encodedHash)
    {
        var parts = encodedHash.Split('.');
        if (parts.Length != 4 ||
            parts[0] != "v1" ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations is < MinAcceptedLegacyIterations or > MaxAcceptedLegacyIterations)
        {
            return PasswordVerificationResult.Failed;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != LegacySaltSize || expected.Length != LegacyKeySize)
            {
                return PasswordVerificationResult.Failed;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, LegacyKeySize);
            return CryptographicOperations.FixedTimeEquals(actual, expected)
                ? PasswordVerificationResult.SuccessRehashNeeded
                : PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}
