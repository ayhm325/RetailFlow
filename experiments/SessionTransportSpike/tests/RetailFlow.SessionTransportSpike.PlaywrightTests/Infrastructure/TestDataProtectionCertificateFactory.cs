using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

internal sealed class TestDataProtectionCertificateFactory : IDisposable
{
    private readonly string _directory;

    public string CertificatePath { get; }

    public string Password { get; }

    private TestDataProtectionCertificateFactory(
        string directory,
        string certificatePath,
        string password)
    {
        _directory = directory;
        CertificatePath = certificatePath;
        Password = password;
    }

    public static TestDataProtectionCertificateFactory Create()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "RetailFlow",
            "PlaywrightDataProtection",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var certificatePath = Path.Combine(directory, "dataprotection.pfx");
        var password = $"RetailFlow-DP-{Guid.NewGuid():N}!";

        using var rsa = RSA.Create(3072);

        var request = new CertificateRequest(
            "CN=RetailFlow-Playwright-DataProtection",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.KeyEncipherment |
                X509KeyUsageFlags.DataEncipherment,
                critical: true));

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        File.WriteAllBytes(
            certificatePath,
            certificate.Export(X509ContentType.Pfx, password));

        return new TestDataProtectionCertificateFactory(
            directory,
            certificatePath,
            password);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of temporary certificate directory.
        }
    }
}
