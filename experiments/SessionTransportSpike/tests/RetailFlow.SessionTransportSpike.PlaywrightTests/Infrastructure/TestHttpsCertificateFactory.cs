using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

internal sealed class TestHttpsCertificateFactory : IDisposable
{
    private readonly string _directory;

    public string CertificatePath { get; }

    public string Password { get; }

    private TestHttpsCertificateFactory(
        string directory,
        string certificatePath,
        string password)
    {
        _directory = directory;
        CertificatePath = certificatePath;
        Password = password;
    }

    public static TestHttpsCertificateFactory Create()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "RetailFlow",
            "PlaywrightHttps",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var certificatePath = Path.Combine(directory, "kestrel-https.pfx");
        var password = $"RetailFlow-Https-{Guid.NewGuid():N}!";

        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(
                certificateAuthority: false,
                hasPathLengthConstraint: false,
                pathLengthConstraint: 0,
                critical: true));

        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature |
                X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") },
                critical: false));

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddIpAddress(IPAddress.Loopback);
        sanBuilder.AddIpAddress(IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(sanBuilder.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(1));

        File.WriteAllBytes(
            certificatePath,
            certificate.Export(X509ContentType.Pfx, password));

        return new TestHttpsCertificateFactory(
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
