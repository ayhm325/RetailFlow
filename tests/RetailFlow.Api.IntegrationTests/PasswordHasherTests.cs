using System.Security.Cryptography;

using Microsoft.AspNetCore.Identity;
using RetailFlow.Infrastructure.Identity;

namespace RetailFlow.Api.IntegrationTests;

public sealed class PasswordHasherTests
{
    private readonly RetailFlowPasswordHasher _hasher = new();

    private static RetailFlowIdentityUser CreateDummyUser() => new()
    {
        Id = Guid.NewGuid(),
        UserName = "testuser",
        Email = "test@example.com",
        DisplayName = "Test User"
    };

    [Fact]
    public void Hash_UsesIdentityV3Format_AndVerifyReturnsExplicitResults()
    {
        var user = CreateDummyUser();
        var hash = _hasher.HashPassword(user, "Str0ngPass!2026");

        Assert.StartsWith("AQAAAA", hash, StringComparison.Ordinal);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user, hash, "Str0ngPass!2026"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, hash, "WrongPass!2026"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, hash, ""));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, "malformed-hash", "Str0ngPass!2026"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, "v1.600000.notbase64.notbase64", "Str0ngPass!2026"));

        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(user, new string('a', RetailFlowPasswordHasher.MaxPasswordLength + 1)));
    }

    [Theory]
    [InlineData(310_000)]
    [InlineData(600_000)]
    public void Verify_LegacyV1Hash_ReturnsSuccessRehashNeeded_AndUpgradesToIdentityV3(int iterations)
    {
        var user = CreateDummyUser();
        var salt = new byte[16];
        RandomNumberGenerator.Fill(salt);
        var legacyDerived = Rfc2898DeriveBytes.Pbkdf2("Leg123", salt, iterations, HashAlgorithmName.SHA256, 32);
        var legacyHash = $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(legacyDerived)}";

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, _hasher.VerifyHashedPassword(user, legacyHash, "Leg123"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, legacyHash, "Wrong!"));

        var upgraded = _hasher.HashPassword(user, "Leg123");
        Assert.StartsWith("AQAAAA", upgraded, StringComparison.Ordinal);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(user, upgraded, "Leg123"));
    }

    [Fact]
    public void Verify_RejectsOutOfRangeIterations_AndInvalidSaltOrKeyLengths()
    {
        var user = CreateDummyUser();
        var validSalt = Convert.ToBase64String(new byte[16]);
        var validKey = Convert.ToBase64String(new byte[32]);
        var shortSalt = Convert.ToBase64String(new byte[8]);
        var shortKey = Convert.ToBase64String(new byte[16]);

        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, $"v1.10000000.{validSalt}.{validKey}", "AnyPass!1"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, $"v1.1000.{validSalt}.{validKey}", "AnyPass!1"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, $"v1.600000.{shortSalt}.{validKey}", "AnyPass!1"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, $"v1.600000.{validSalt}.{shortKey}", "AnyPass!1"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(user, $"v1.600000.{validSalt}.{validKey}", new string('x', RetailFlowPasswordHasher.MaxPasswordLength + 1)));
    }
}
