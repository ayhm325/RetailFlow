using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_Uses600000Iterations_AndVerifiesValidAndInvalidPasswords()
    {
        var hash = PasswordHasherService.Hash("Str0ngPass!2026");

        Assert.StartsWith("v1.600000.", hash, StringComparison.Ordinal);
        Assert.True(PasswordHasherService.Verify("Str0ngPass!2026", hash));
        Assert.False(PasswordHasherService.Verify("WrongPass!2026", hash));
        Assert.False(PasswordHasherService.Verify("", hash));
        Assert.False(PasswordHasherService.Verify("Str0ngPass!2026", "malformed-hash"));
        Assert.False(PasswordHasherService.Verify("Str0ngPass!2026", "v1.600000.notbase64.notbase64"));

        Assert.Throws<ArgumentException>(() => PasswordHasherService.Hash("short"));
    }

    [Fact]
    public void Verify_SupportsLegacy310000IterationFormat()
    {
        var salt = new byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        var legacyDerived = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            "LegacyPass!1",
            salt,
            310_000,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            32);
        var legacyHash = $"v1.310000.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(legacyDerived)}";

        Assert.True(PasswordHasherService.Verify("LegacyPass!1", legacyHash));
        Assert.False(PasswordHasherService.Verify("WrongLegacy!1", legacyHash));
    }
}
