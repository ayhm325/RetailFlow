using System.Security.Cryptography;

namespace RetailFlow.Infrastructure.Identity;

internal enum PasswordHashVerificationResult
{
    Failed = 0,
    Success = 1,
    SuccessRehashNeeded = 2,
    InvalidHashFormat = 3
}

/// <summary>
/// OWASP Password Storage Cheat Sheet compliant PBKDF2-HMAC-SHA256 (600,000 iterations, 128-bit salt, constant-time compare).
/// </summary>
internal static class PasswordHasherService
{
    public const int MaxPasswordLength = 1024;
    private const int MinNewPasswordLength = 8;
    private const int SaltSize = 16;
    private const int KeySize = 32;
    private const int CurrentIterations = 600_000;
    private const int MinAcceptedIterations = 310_000;
    private const int MaxAcceptedIterations = 1_000_000;

    /// <summary>
    /// Hashes a new password enforcing new-password creation policy (8..1024 chars, Unicode preserved).
    /// </summary>
    public static string Hash(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinNewPasswordLength)
        {
            throw new ArgumentException($"Password must be at least {MinNewPasswordLength} characters.", nameof(password));
        }

        if (password.Length > MaxPasswordLength)
        {
            throw new ArgumentException($"Password cannot exceed {MaxPasswordLength} characters.", nameof(password));
        }

        return HashCore(password);
    }

    /// <summary>
    /// Rehashes an already-verified legacy password without applying new-password minimum length rules.
    /// </summary>
    public static string RehashVerifiedPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            throw new ArgumentException("Verified password length is invalid for rehash.", nameof(password));
        }

        return HashCore(password);
    }

    public static PasswordHashVerificationResult Verify(string password, string encodedHash)
    {
        if (string.IsNullOrEmpty(password) || password.Length > MaxPasswordLength)
        {
            return PasswordHashVerificationResult.Failed;
        }

        if (string.IsNullOrWhiteSpace(encodedHash))
        {
            return PasswordHashVerificationResult.InvalidHashFormat;
        }

        var parts = encodedHash.Split('.');
        if (parts.Length != 4 ||
            parts[0] != "v1" ||
            !int.TryParse(parts[1], out var iterations) ||
            iterations is < MinAcceptedIterations or > MaxAcceptedIterations)
        {
            return PasswordHashVerificationResult.InvalidHashFormat;
        }

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            if (salt.Length != SaltSize || expected.Length != KeySize)
            {
                return PasswordHashVerificationResult.InvalidHashFormat;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                return PasswordHashVerificationResult.Failed;
            }

            return iterations < CurrentIterations
                ? PasswordHashVerificationResult.SuccessRehashNeeded
                : PasswordHashVerificationResult.Success;
        }
        catch (FormatException)
        {
            return PasswordHashVerificationResult.InvalidHashFormat;
        }
    }

    private static string HashCore(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, CurrentIterations, HashAlgorithmName.SHA256, KeySize);
        return $"v1.{CurrentIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
}
