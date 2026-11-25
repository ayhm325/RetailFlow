using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.DataProtection;

public static class DataProtectionCertificateLoader
{
    public static X509Certificate2 Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var configuredPath = configuration[
            "DataProtection:Certificate:Path"];

        var password = configuration[
            "DataProtection:Certificate:Password"];

        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException(
                "Data Protection certificate path is required.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "Data Protection certificate password is required.");
        }

        var path = Path.GetFullPath(configuredPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                "Data Protection certificate was not found.",
                path);
        }

        X509Certificate2 certificate;

        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                path,
                password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                "Could not load the Data Protection certificate.",
                exception);
        }

        try
        {
            if (!certificate.HasPrivateKey)
            {
                throw new InvalidOperationException(
                    "The certificate must contain its private key.");
            }

            using var publicKey = certificate.GetRSAPublicKey();
            using var privateKey = certificate.GetRSAPrivateKey();

            if (publicKey is null || privateKey is null)
            {
                throw new InvalidOperationException(
                    "The Data Protection certificate must use RSA " +
                    "and provide an accessible private key.");
            }

            if (publicKey.KeySize < 2048)
            {
                throw new InvalidOperationException(
                    "The certificate RSA key size must be at least 2048 bits.");
            }

            var now = DateTime.UtcNow;

            if (now < certificate.NotBefore.ToUniversalTime() ||
                now > certificate.NotAfter.ToUniversalTime())
            {
                throw new InvalidOperationException(
                    "The Data Protection certificate is not currently valid.");
            }

            return certificate;
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }
}
