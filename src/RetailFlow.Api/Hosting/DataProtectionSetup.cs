using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Options;

using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Hosting;

public sealed class RetailFlowDataProtectionOptions
{
    public const string SectionName = "DataProtection";
    public const string ApplicationName = "RetailFlow";

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public string? CertificateThumbprint { get; set; }

    public List<DataProtectionPreviousCertificateOptions> PreviousCertificates { get; set; } = [];

    /// <summary>
    /// Defaults to true so Production fails closed when Data Protection encryption-at-rest is not configured.
    /// </summary>
    public bool RequireCertificateInProduction { get; set; } = true;

    public int DefaultKeyLifetimeDays { get; set; } = 90;
}

public sealed class DataProtectionPreviousCertificateOptions
{
    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public string? CertificateThumbprint { get; set; }
}

internal static class DataProtectionSetup
{
    public static IServiceCollection AddRetailFlowDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        TimeProvider? timeProvider = null)
    {
        var effectiveTimeProvider = timeProvider ?? TimeProvider.System;
        var section = configuration.GetSection(RetailFlowDataProtectionOptions.SectionName);
        services.Configure<RetailFlowDataProtectionOptions>(section);

        services.AddSingleton<IValidateOptions<RetailFlowDataProtectionOptions>>(
            sp => new RetailFlowDataProtectionOptionsValidator(
                environment,
                timeProvider ?? sp.GetService<TimeProvider>() ?? TimeProvider.System));
        services.AddOptions<RetailFlowDataProtectionOptions>().ValidateOnStart();

        services.AddDataProtection()
            .SetApplicationName(RetailFlowDataProtectionOptions.ApplicationName)
            .PersistKeysToDbContext<RetailFlowDbContext>();

        // Configure KeyManagementOptions and ICertificateResolver lazily from IOptions<RetailFlowDataProtectionOptions>
        // so late configuration providers (e.g., WebApplicationFactory, post-configure) are always honored.
        services.AddSingleton<ICertificateResolver>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<RetailFlowDataProtectionOptions>>().Value;
            var tp = timeProvider ?? sp.GetService<TimeProvider>() ?? TimeProvider.System;
            var certs = new Dictionary<string, X509Certificate2>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(opts.CertificatePath))
            {
                var primaryCert = LoadAndValidateCertificate(
                    opts.CertificatePath,
                    opts.CertificatePassword,
                    opts.CertificateThumbprint,
                    requireCurrentlyValidWindow: true,
                    tp.GetUtcNow(),
                    "primary");
                certs[NormalizeThumbprint(primaryCert.Thumbprint)] = primaryCert;

                for (var i = 0; i < opts.PreviousCertificates.Count; i++)
                {
                    var prev = opts.PreviousCertificates[i];
                    if (string.IsNullOrWhiteSpace(prev.CertificatePath))
                    {
                        continue;
                    }

                    // Previous rotation certificates may be expired; they are still needed to unprotect historical keys.
                    var prevCert = LoadAndValidateCertificate(
                        prev.CertificatePath,
                        prev.CertificatePassword,
                        prev.CertificateThumbprint,
                        requireCurrentlyValidWindow: false,
                        tp.GetUtcNow(),
                        $"previous[{i}]");
                    certs[NormalizeThumbprint(prevCert.Thumbprint)] = prevCert;
                }
            }

            return new RetailFlowCertificateResolver(certs);
        });

        services.AddOptions<KeyManagementOptions>()
            .Configure<IOptions<RetailFlowDataProtectionOptions>, ILoggerFactory, IServiceProvider>((kmOptions, dpOptionsAccessor, loggerFactory, sp) =>
            {
                var opts = dpOptionsAccessor.Value;
                var tp = timeProvider ?? sp.GetService<TimeProvider>() ?? TimeProvider.System;
                kmOptions.NewKeyLifetime = TimeSpan.FromDays(Math.Clamp(opts.DefaultKeyLifetimeDays, 7, 365));

                if (!string.IsNullOrWhiteSpace(opts.CertificatePath))
                {
                    var primaryCert = LoadAndValidateCertificate(
                        opts.CertificatePath,
                        opts.CertificatePassword,
                        opts.CertificateThumbprint,
                        requireCurrentlyValidWindow: true,
                        tp.GetUtcNow(),
                        "primary");

                    kmOptions.XmlEncryptor = new RetailFlowCertificateXmlEncryptor(primaryCert, loggerFactory);
                }
            });

        services.AddHostedService<DataProtectionDiagnosticsHostedService>();

        return services;
    }

    private sealed class RetailFlowCertificateXmlEncryptor(
        X509Certificate2 primaryCertificate,
        ILoggerFactory loggerFactory) : IXmlEncryptor
    {
        private readonly CertificateXmlEncryptor _inner = new(primaryCertificate, loggerFactory);

        public EncryptedXmlInfo Encrypt(XElement plaintextElement)
        {
            var innerInfo = _inner.Encrypt(plaintextElement);
            return new EncryptedXmlInfo(innerInfo.EncryptedElement, typeof(RetailFlowCertificateXmlDecryptor));
        }
    }

    private sealed class RetailFlowCertificateXmlDecryptor(
        IServiceProvider services) : IXmlDecryptor
    {
        private readonly ICertificateResolver _certificateResolver = services.GetRequiredService<ICertificateResolver>();

        public XElement Decrypt(XElement encryptedElement)
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            using (var reader = encryptedElement.CreateReader())
            {
                doc.Load(reader);
            }

            var encryptedXml = new ResolverAwareEncryptedXml(doc, _certificateResolver);
            encryptedXml.DecryptDocument();

            using var nodeReader = new XmlNodeReader(doc.DocumentElement!);
            return XElement.Load(nodeReader);
        }

        private sealed class ResolverAwareEncryptedXml(
            XmlDocument document,
            ICertificateResolver certificateResolver) : EncryptedXml(document)
        {
            public override byte[]? DecryptEncryptedKey(EncryptedKey encryptedKey)
            {
                if (encryptedKey?.KeyInfo is not null && encryptedKey.CipherData?.CipherValue is not null)
                {
                    var useOaep = string.Equals(
                        encryptedKey.EncryptionMethod?.KeyAlgorithm,
                        XmlEncRSAOAEPUrl,
                        StringComparison.Ordinal);

                    foreach (var clause in encryptedKey.KeyInfo)
                    {
                        if (clause is KeyInfoX509Data x509Data && x509Data.Certificates is not null)
                        {
                            foreach (var entry in x509Data.Certificates)
                            {
                                if (entry is X509Certificate2 embeddedCert)
                                {
                                    var resolved = certificateResolver.ResolveCertificate(embeddedCert.Thumbprint);
                                    if (resolved is not null)
                                    {
                                        using var rsa = resolved.GetRSAPrivateKey();
                                        if (rsa is not null)
                                        {
                                            return DecryptKey(encryptedKey.CipherData.CipherValue, rsa, useOaep);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                return base.DecryptEncryptedKey(encryptedKey!);
            }
        }
    }

    private sealed class RetailFlowCertificateResolver(
        IReadOnlyDictionary<string, X509Certificate2> configuredCertificates) : ICertificateResolver, IDisposable
    {
        public X509Certificate2? ResolveCertificate(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                return null;
            }

            var normalized = NormalizeThumbprint(thumbprint);
            if (configuredCertificates.TryGetValue(normalized, out var cert))
            {
                return cert;
            }

            return null;
        }

        public void Dispose()
        {
            foreach (var cert in configuredCertificates.Values)
            {
                cert.Dispose();
            }
        }
    }

    internal static X509Certificate2 LoadAndValidateCertificate(
        string certificatePath,
        string? password,
        string? expectedThumbprint,
        bool requireCurrentlyValidWindow,
        DateTimeOffset utcNow,
        string roleLabel)
    {
        if (!File.Exists(certificatePath))
        {
            throw new InvalidOperationException(
                $"DataProtection {roleLabel} certificate file was not found.");
        }

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"DataProtection {roleLabel} certificate could not be loaded. Verify file format and password.",
                ex);
        }

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                $"DataProtection {roleLabel} certificate does not contain a private key.");
        }

        // Verify the private key is actually accessible for cryptographic operations.
        try
        {
            using var rsa = certificate.GetRSAPrivateKey();
            using var ecdsa = certificate.GetECDsaPrivateKey();
            if (rsa is null && ecdsa is null)
            {
                certificate.Dispose();
                throw new InvalidOperationException(
                    $"DataProtection {roleLabel} certificate private key is not an accessible RSA or ECDSA key.");
            }
        }
        catch (CryptographicException ex)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                $"DataProtection {roleLabel} certificate private key is not accessible.",
                ex);
        }

        if (requireCurrentlyValidWindow)
        {
            var notBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
            var notAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
            if (utcNow < notBefore || utcNow > notAfter)
            {
                certificate.Dispose();
                throw new InvalidOperationException(
                    $"DataProtection {roleLabel} certificate is outside its validity window ({notBefore:u}..{notAfter:u}).");
            }
        }

        if (!string.IsNullOrWhiteSpace(expectedThumbprint))
        {
            var normalizedExpected = NormalizeThumbprint(expectedThumbprint);
            var sha1Thumbprint = NormalizeThumbprint(certificate.Thumbprint);
            var sha256Thumbprint = NormalizeThumbprint(certificate.GetCertHashString(HashAlgorithmName.SHA256));

            if (!string.Equals(normalizedExpected, sha1Thumbprint, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(normalizedExpected, sha256Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                certificate.Dispose();
                throw new InvalidOperationException(
                    $"DataProtection {roleLabel} certificate thumbprint does not match the configured CertificateThumbprint.");
            }
        }

        return certificate;
    }

    private static string NormalizeThumbprint(string value) =>
        value.Replace(" ", string.Empty, StringComparison.Ordinal)
             .Replace(":", string.Empty, StringComparison.Ordinal)
             .Trim();

    private sealed class RetailFlowDataProtectionOptionsValidator(
        IHostEnvironment environment,
        TimeProvider timeProvider) : IValidateOptions<RetailFlowDataProtectionOptions>
    {
        public ValidateOptionsResult Validate(string? name, RetailFlowDataProtectionOptions options)
        {
            if (options.DefaultKeyLifetimeDays is < 7 or > 365)
            {
                return ValidateOptionsResult.Fail(
                    "DataProtection:DefaultKeyLifetimeDays must be between 7 and 365.");
            }

            if (environment.IsProduction() && !options.RequireCertificateInProduction)
            {
                return ValidateOptionsResult.Fail(
                    "DataProtection:RequireCertificateInProduction cannot be disabled in Production.");
            }

            if (environment.IsProduction() &&
                string.IsNullOrWhiteSpace(options.CertificatePath))
            {
                return ValidateOptionsResult.Fail(
                    "DataProtection:CertificatePath is required in Production when RequireCertificateInProduction is enabled.");
            }

            if (!string.IsNullOrWhiteSpace(options.CertificatePath))
            {
                try
                {
                    using var cert = LoadAndValidateCertificate(
                        options.CertificatePath,
                        options.CertificatePassword,
                        options.CertificateThumbprint,
                        requireCurrentlyValidWindow: true,
                        timeProvider.GetUtcNow(),
                        "primary");

                    for (var i = 0; i < options.PreviousCertificates.Count; i++)
                    {
                        var prev = options.PreviousCertificates[i];
                        if (string.IsNullOrWhiteSpace(prev.CertificatePath))
                        {
                            continue;
                        }

                        using var prevCert = LoadAndValidateCertificate(
                            prev.CertificatePath,
                            prev.CertificatePassword,
                            prev.CertificateThumbprint,
                            requireCurrentlyValidWindow: false,
                            timeProvider.GetUtcNow(),
                            $"previous[{i}]");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    return ValidateOptionsResult.Fail(ex.Message);
                }
            }

            return ValidateOptionsResult.Success;
        }
    }

    private sealed class DataProtectionDiagnosticsHostedService(
        IOptions<RetailFlowDataProtectionOptions> optionsAccessor,
        IHostEnvironment environment,
        ILogger<DataProtectionDiagnosticsHostedService> logger) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            var opts = optionsAccessor.Value;
            var hasCert = !string.IsNullOrWhiteSpace(opts.CertificatePath);
            var previousCount = opts.PreviousCertificates.Count(p => !string.IsNullOrWhiteSpace(p.CertificatePath));

            if (hasCert)
            {
                logger.LogInformation(
                    "DataProtection initialized: Environment={Environment}, Repository=SqlServerDbContext(DataProtectionKeys), ProtectionProvider=X509Certificate, ApplicationName={ApplicationName}, DefaultKeyLifetimeDays={DefaultKeyLifetimeDays}, PreviousCertificates={PreviousCount}",
                    environment.EnvironmentName,
                    RetailFlowDataProtectionOptions.ApplicationName,
                    opts.DefaultKeyLifetimeDays,
                    previousCount);
            }
            else
            {
                logger.LogWarning(
                    "DataProtection initialized WITHOUT encryption-at-rest certificate: Environment={Environment}, Repository=SqlServerDbContext(DataProtectionKeys), ProtectionProvider=None, ApplicationName={ApplicationName}, RequireCertificateInProduction={RequireCertificateInProduction}",
                    environment.EnvironmentName,
                    RetailFlowDataProtectionOptions.ApplicationName,
                    opts.RequireCertificateInProduction);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
