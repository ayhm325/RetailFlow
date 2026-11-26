using System.Net;
using System.Security.Cryptography;
using System.Text;

using Microsoft.AspNetCore.DataProtection;

namespace RetailFlow.Infrastructure.Identity;

/// <summary>
/// Provides Data Protection encryption for TOTP secrets at rest and RFC 6238 TOTP verification
/// returning the exact matched 30-second time step for replay prevention.
/// </summary>
internal static class TotpCryptoHelper
{
    public const string DataProtectionPurpose = "RetailFlow.Identity.TotpSecret.v1";
    private const string ProtectedPrefix = "dp:v1:";
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int TotpStepSeconds = 30;
    private const int TotpDigits = 6;
    private const int AllowedTimeStepDrift = 1;

    public static string GenerateBase32Secret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return EncodeBase32(bytes);
    }

    public static string ProtectSecret(IDataProtectionProvider dataProtectionProvider, string plaintextSecret)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintextSecret);

        if (plaintextSecret.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return plaintextSecret;
        }

        var protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        return ProtectedPrefix + protector.Protect(plaintextSecret);
    }

    public static string UnprotectSecret(IDataProtectionProvider dataProtectionProvider, string storedValue)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(storedValue);

        if (!storedValue.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return storedValue;
        }

        var protector = dataProtectionProvider.CreateProtector(DataProtectionPurpose);
        return protector.Unprotect(storedValue[ProtectedPrefix.Length..]);
    }

    public static string FormatSharedKey(string unformattedKey)
    {
        var result = new StringBuilder();
        var currentPosition = 0;
        while (currentPosition + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition, 4)).Append(' ');
            currentPosition += 4;
        }

        if (currentPosition < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(currentPosition));
        }

        return result.ToString().ToLowerInvariant();
    }

    public static bool VerifyCodeWithTimeStep(
        string base32Secret,
        string? code,
        DateTimeOffset utcNow,
        out long matchedTimeStep)
    {
        matchedTimeStep = 0;
        if (string.IsNullOrWhiteSpace(base32Secret) || string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        var sanitized = code.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal);

        if (sanitized.Length != TotpDigits || !int.TryParse(sanitized, out var providedCode))
        {
            return false;
        }

        if (!TryDecodeBase32(base32Secret, out var keyBytes))
        {
            return false;
        }

        var currentStep = utcNow.ToUnixTimeSeconds() / TotpStepSeconds;

        for (var offset = -AllowedTimeStepDrift; offset <= AllowedTimeStepDrift; offset++)
        {
            var candidateStep = currentStep + offset;
            if (candidateStep < 0)
            {
                continue;
            }

            var expectedCode = ComputeTotp(keyBytes, candidateStep);
            if (expectedCode == providedCode)
            {
                matchedTimeStep = candidateStep;
                return true;
            }
        }

        return false;
    }

    public static int ComputeTotpForTime(string base32Secret, DateTimeOffset utcNow)
    {
        if (!TryDecodeBase32(base32Secret, out var keyBytes))
        {
            throw new InvalidOperationException("Invalid Base32 TOTP secret.");
        }

        var step = utcNow.ToUnixTimeSeconds() / TotpStepSeconds;
        return ComputeTotp(keyBytes, step);
    }

    private static int ComputeTotp(byte[] keyBytes, long timeStep)
    {
        Span<byte> counterBytes = stackalloc byte[8];
        var networkOrder = IPAddress.HostToNetworkOrder(timeStep);
        BitConverter.TryWriteBytes(counterBytes, networkOrder);

        Span<byte> hash = stackalloc byte[20];
        HMACSHA1.HashData(keyBytes, counterBytes, hash);

        var offset = hash[^1] & 0x0F;
        var binaryCode =
            ((hash[offset] & 0x7F) << 24) |
            ((hash[offset + 1] & 0xFF) << 16) |
            ((hash[offset + 2] & 0xFF) << 8) |
            (hash[offset + 3] & 0xFF);

        return binaryCode % 1_000_000;
    }

    private static string EncodeBase32(ReadOnlySpan<byte> data)
    {
        var result = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 0x1F]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 0x1F]);
        }

        return result.ToString();
    }

    private static bool TryDecodeBase32(string base32, out byte[] bytes)
    {
        var cleaned = base32.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        if (cleaned.Length == 0)
        {
            bytes = [];
            return false;
        }

        var output = new List<byte>((cleaned.Length * 5) / 8);
        int buffer = 0;
        int bitsLeft = 0;

        foreach (var c in cleaned)
        {
            if (c == '=')
            {
                break;
            }

            var val = Base32Alphabet.IndexOf(c);
            if (val < 0)
            {
                bytes = [];
                return false;
            }

            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xFF));
                bitsLeft -= 8;
            }
        }

        bytes = [.. output];
        return bytes.Length > 0;
    }
}
