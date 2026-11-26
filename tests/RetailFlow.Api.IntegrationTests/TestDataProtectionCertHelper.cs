using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RetailFlow.Api.IntegrationTests;

internal static class TestDataProtectionCertHelper
{
    private static readonly Lazy<(string Path, string Password, string Thumbprint)> SharedCert = new(() =>
    {
        const string password = "TestDpCertPass!2026";
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=RetailFlow-Test-DP", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
        var pfxBytes = cert.Export(X509ContentType.Pfx, password);
        var path = Path.Combine(Path.GetTempPath(), $"rf-shared-dp-{Environment.ProcessId}.pfx");
        File.WriteAllBytes(path, pfxBytes);
        return (path, password, cert.Thumbprint);
    });

    public static void ApplyTestCertificate(IDictionary<string, string?> config)
    {
        var (path, password, thumbprint) = SharedCert.Value;
        config["DataProtection:CertificatePath"] = path;
        config["DataProtection:CertificatePassword"] = password;
        config["DataProtection:CertificateThumbprint"] = thumbprint;
    }
}
