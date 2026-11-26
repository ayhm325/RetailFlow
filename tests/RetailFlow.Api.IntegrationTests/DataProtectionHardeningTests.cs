using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.IntegrationTests;

[Collection("DataProtectionSql")]
public sealed class DataProtectionHardeningTests : IClassFixture<CatalogSqlTestFixture>
{
    private readonly CatalogSqlTestFixture _fixture;

    public DataProtectionHardeningTests(CatalogSqlTestFixture fixture)
    {
        _fixture = fixture;
    }

    private static string CreateTempPfx(
        string subjectName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        string password,
        out string thumbprint)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(notBefore, notAfter);
        thumbprint = cert.Thumbprint;
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        var path = Path.Combine(Path.GetTempPath(), $"rf-dp-{Guid.NewGuid():N}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        return path;
    }

    private static string CreateTempPublicOnlyCert(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest($"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var derBytes = cert.Export(X509ContentType.Cert);
        var path = Path.Combine(Path.GetTempPath(), $"rf-dp-pub-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(path, derBytes);
        return path;
    }

    private WebApplicationFactory<Program> CreateProductionDpFactory(Dictionary<string, string?> settings)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var baseValues = new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["Email:RequireConfiguredInProduction"] = "false"
                    };

                    foreach (var kvp in settings)
                    {
                        baseValues[kvp.Key] = kvp.Value;
                    }

                    config.AddInMemoryCollection(baseValues);
                });
            });
    }

    [Fact]
    public void TestA_ProductionWithoutCertificate_FailsStartupSafely()
    {
        using var factory = CreateProductionDpFactory(new Dictionary<string, string?>());

        var ex = Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });

        Assert.Contains("DataProtection:CertificatePath", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Password", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestB_DevelopmentWithoutCertificate_StartsNormally()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Development);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString
                    });
                });
            });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync("/health/live");
        Assert.Equal(System.Net.HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public void TestC_InvalidCertificates_AreRejectedInProduction()
    {
        const string pass = "TestCertPass!1";
        var expiredPath = CreateTempPfx("Expired", DateTimeOffset.UtcNow.AddDays(-10), DateTimeOffset.UtcNow.AddDays(-1), pass, out _);
        var futurePath = CreateTempPfx("Future", DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(30), pass, out _);
        var validPath = CreateTempPfx("Valid", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), pass, out _);
        var publicOnlyPath = CreateTempPublicOnlyCert("PublicOnly");

        try
        {
            // 1. Missing certificate file
            using (var fMissing = CreateProductionDpFactory(new()
            {
                ["DataProtection:CertificatePath"] = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.pfx"),
                ["DataProtection:CertificatePassword"] = pass
            }))
            {
                Assert.ThrowsAny<Exception>(() => { using var _ = fMissing.CreateClient(); });
            }

            // 2. Wrong thumbprint
            using (var fThumb = CreateProductionDpFactory(new()
            {
                ["DataProtection:CertificatePath"] = validPath,
                ["DataProtection:CertificatePassword"] = pass,
                ["DataProtection:CertificateThumbprint"] = "0000000000000000000000000000000000000000"
            }))
            {
                Assert.ThrowsAny<Exception>(() => { using var _ = fThumb.CreateClient(); });
            }

            // 3. Expired certificate
            using (var fExpired = CreateProductionDpFactory(new()
            {
                ["DataProtection:CertificatePath"] = expiredPath,
                ["DataProtection:CertificatePassword"] = pass
            }))
            {
                Assert.ThrowsAny<Exception>(() => { using var _ = fExpired.CreateClient(); });
            }

            // 4. Not-yet-valid certificate
            using (var fFuture = CreateProductionDpFactory(new()
            {
                ["DataProtection:CertificatePath"] = futurePath,
                ["DataProtection:CertificatePassword"] = pass
            }))
            {
                Assert.ThrowsAny<Exception>(() => { using var _ = fFuture.CreateClient(); });
            }

            // 5. Public-only certificate without private key
            using (var fPub = CreateProductionDpFactory(new()
            {
                ["DataProtection:CertificatePath"] = publicOnlyPath
            }))
            {
                Assert.ThrowsAny<Exception>(() => { using var _ = fPub.CreateClient(); });
            }
        }
        finally
        {
            foreach (var p in new[] { expiredPath, futurePath, validPath, publicOnlyPath })
            {
                if (File.Exists(p)) File.Delete(p);
            }
        }
    }

    [Fact]
    public async Task TestD_E_F_G_ProtectUnprotect_RestartPersistence_MultiInstance_And_CertificateRotation()
    {
        const string pass1 = "RotPass1!2026";
        const string pass2 = "RotPass2!2026";
        var cert1Path = CreateTempPfx("RF-Cert-1", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), pass1, out var thumb1);
        var cert2Path = CreateTempPfx("RF-Cert-2", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(60), pass2, out var thumb2);

        try
        {
            var dpSettings1 = new Dictionary<string, string?>
            {
                ["DataProtection:CertificatePath"] = cert1Path,
                ["DataProtection:CertificatePassword"] = pass1,
                ["DataProtection:CertificateThumbprint"] = thumb1
            };

            string protectedByInstance1;
            const string payload1 = "sensitive-tenant-payload-v1";

            // Test D & E: Instance 1 starts, protects payload, then shuts down
            await using (var instance1 = CreateProductionDpFactory(dpSettings1))
            {
                using var _ = instance1.CreateClient();
                var protector1 = instance1.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("RetailFlow.Test.Hardening");
                protectedByInstance1 = protector1.Protect(payload1);
                Assert.NotEqual(payload1, protectedByInstance1);
            }

            // Test E & F: Instance A and Instance B start fresh against same SQL Key Ring and cross-unprotect
            await using (var instanceA = CreateProductionDpFactory(dpSettings1))
            await using (var instanceB = CreateProductionDpFactory(dpSettings1))
            {
                using var _a = instanceA.CreateClient();
                using var _b = instanceB.CreateClient();

                var protA = instanceA.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("RetailFlow.Test.Hardening");
                var protB = instanceB.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("RetailFlow.Test.Hardening");

                Assert.Equal(payload1, protA.Unprotect(protectedByInstance1));
                Assert.Equal(payload1, protB.Unprotect(protectedByInstance1));

                var fromA = protA.Protect("msg-from-A");
                var fromB = protB.Protect("msg-from-B");
                Assert.Equal("msg-from-A", protB.Unprotect(fromA));
                Assert.Equal("msg-from-B", protA.Unprotect(fromB));
            }

            // Test G: Certificate Rotation (New primary cert2, previous cert1 in PreviousCertificates)
            var rotationSettings = new Dictionary<string, string?>
            {
                ["DataProtection:CertificatePath"] = cert2Path,
                ["DataProtection:CertificatePassword"] = pass2,
                ["DataProtection:CertificateThumbprint"] = thumb2,
                ["DataProtection:PreviousCertificates:0:CertificatePath"] = cert1Path,
                ["DataProtection:PreviousCertificates:0:CertificatePassword"] = pass1,
                ["DataProtection:PreviousCertificates:0:CertificateThumbprint"] = thumb1
            };

            await using (var rotatedInstance = CreateProductionDpFactory(rotationSettings))
            {
                using var _r = rotatedInstance.CreateClient();
                var protRotated = rotatedInstance.Services.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector("RetailFlow.Test.Hardening");

                // Old data protected under cert1 is still unprotected after rotation
                Assert.Equal(payload1, protRotated.Unprotect(protectedByInstance1));

                // Force creation of a new key encrypted with cert2
                var keyManager = rotatedInstance.Services.GetRequiredService<IKeyManager>();
                var newKey = keyManager.CreateNewKey(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(90));
                var newProtected = protRotated.Protect("msg-after-rotation");
                Assert.Equal("msg-after-rotation", protRotated.Unprotect(newProtected));

                await using var db = _fixture.CreateDbContext();
                var storedNewKey = await db.DataProtectionKeys
                    .AsNoTracking()
                    .SingleAsync(k => k.FriendlyName == $"key-{newKey.KeyId}");
                Assert.NotNull(storedNewKey.Xml);
                Assert.True(storedNewKey.Xml.Contains("EncryptedData", StringComparison.Ordinal), $"Unexpected key XML: {storedNewKey.Xml}");
                Assert.Contains("CipherValue", storedNewKey.Xml, StringComparison.Ordinal);
            }
        }
        finally
        {
            if (File.Exists(cert1Path)) File.Delete(cert1Path);
            if (File.Exists(cert2Path)) File.Delete(cert2Path);
        }
    }

    [Fact]
    public void TestH_ConfigurationPrecedence_HigherPriorityProviderOverridesDefault()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment(Environments.Production);
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["AllowedHosts"] = "retail.example.com",
                        ["RETAILFLOW_CONNECTION"] = _fixture.ConnectionString,
                        ["ConnectionStrings:RetailFlow"] = _fixture.ConnectionString,
                        ["DataProtection:RequireCertificateInProduction"] = "false",
                        ["Email:RequireConfiguredInProduction"] = "false"
                    });
                    // Higher-priority configuration source re-enforces true
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DataProtection:RequireCertificateInProduction"] = "true"
                    });
                });
            });

        Assert.Throws<OptionsValidationException>(() =>
        {
            using var _ = factory.CreateClient();
        });
    }
}
