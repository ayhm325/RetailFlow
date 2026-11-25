using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

internal sealed record TestDataProtectionCertificate(
    string Path,
    string Password);

internal static class TestDataProtectionCertificates
{
    private const string CertificatePassword =
        "RetailFlow-Integration-Only-2026!";

    private static readonly Lazy<TestDataProtectionCertificate>
        SharedCertificate = new(
            () => Create("RetailFlow-Test-Shared"));

    private static readonly Lazy<TestDataProtectionCertificate>
        WrongCertificate = new(
            () => Create("RetailFlow-Test-Wrong"));

    public static TestDataProtectionCertificate Shared =>
        SharedCertificate.Value;

    public static TestDataProtectionCertificate Wrong =>
        WrongCertificate.Value;

    private static TestDataProtectionCertificate Create(string subject)
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "RetailFlow",
            "DataProtectionTests",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);

        var certificatePath = Path.Combine(
            directory,
            "dataprotection.pfx");

        using var rsa = RSA.Create(3072);

        var request = new CertificateRequest(
            $"CN={subject}",
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
            DateTimeOffset.UtcNow.AddYears(3));

        File.WriteAllBytes(
            certificatePath,
            certificate.Export(
                X509ContentType.Pfx,
                CertificatePassword));

        return new TestDataProtectionCertificate(
            certificatePath,
            CertificatePassword);
    }
}
