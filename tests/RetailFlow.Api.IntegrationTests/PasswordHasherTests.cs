using System.Security.Cryptography;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_Uses600000Iterations_AndVerifyReturnsExplicitResults()
    {
        var hash = PasswordHasherService.Hash("Str0ngPass!2026");

        Assert.StartsWith("v1.600000.", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordHashVerificationResult.Success, PasswordHasherService.Verify("Str0ngPass!2026", hash));
        Assert.Equal(PasswordHashVerificationResult.Failed, PasswordHasherService.Verify("WrongPass!2026", hash));
        Assert.Equal(PasswordHashVerificationResult.Failed, PasswordHasherService.Verify("", hash));
        Assert.Equal(PasswordHashVerificationResult.InvalidHashFormat, PasswordHasherService.Verify("Str0ngPass!2026", "malformed-hash"));
        Assert.Equal(PasswordHashVerificationResult.InvalidHashFormat, PasswordHasherService.Verify("Str0ngPass!2026", "v1.600000.notbase64.notbase64"));

        Assert.Throws<ArgumentException>(() => PasswordHasherService.Hash("short"));
        Assert.Throws<ArgumentException>(() => PasswordHasherService.Hash(new string('a', PasswordHasherService.MaxPasswordLength + 1)));
    }

    [Fact]
    public void Verify_Legacy310000Hash_ReturnsSuccessRehashNeeded_AndRehashSupportsLegacyLength()
    {
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        // Legacy 6-char password (< 8 new policy)
        var legacyDerived = Rfc2898DeriveBytes.Pbkdf2("Leg123", salt, 310_000, HashAlgorithmName.SHA256, 32);
        var legacyHash = $"v1.310000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(legacyDerived)}";

        Assert.Equal(PasswordHashVerificationResult.SuccessRehashNeeded, PasswordHasherService.Verify("Leg123", legacyHash));
        Assert.Equal(PasswordHashVerificationResult.Failed, PasswordHasherService.Verify("Wrong!", legacyHash));

        var upgraded = PasswordHasherService.RehashVerifiedPassword("Leg123");
        Assert.StartsWith("v1.600000.", upgraded, StringComparison.Ordinal);
        Assert.Equal(PasswordHashVerificationResult.Success, PasswordHasherService.Verify("Leg123", upgraded));
    }

    [Fact]
    public void Verify_RejectsOutOfRangeIterations_AndInvalidSaltOrKeyLengths()
    {
        var validSalt = Convert.ToBase64String(new byte[16]);
        var validKey = Convert.ToBase64String(new byte[32]);
        var shortSalt = Convert.ToBase64String(new byte[8]);
        var shortKey = Convert.ToBase64String(new byte[16]);

        Assert.Equal(PasswordHashVerificationResult.InvalidHashFormat, PasswordHasherService.Verify("AnyPass!1", $"v1.10000000.{validSalt}.{validKey}"));
        Assert.Equal(PasswordHashVerificationResult.InvalidHashFormat, PasswordHasherService.Verify("AnyPass!1", $"v1.1000.{validSalt}.{validKey}"));
        Assert.Equal(PasswordHashVerificationResult.InvalidHashFormat, PasswordHasherService.Verify("AnyPass!1", $"v1.600000.{shortSalt}.{validKey}"));
        Assert.Equal(PasswordHashVerificationResult.InvalidHashFormat, PasswordHasherService.Verify("AnyPass!1", $"v1.600000.{validSalt}.{shortKey}"));
        Assert.Equal(PasswordHashVerificationResult.Failed, PasswordHasherService.Verify(new string('x', PasswordHasherService.MaxPasswordLength + 1), $"v1.600000.{validSalt}.{validKey}"));
    }
}
