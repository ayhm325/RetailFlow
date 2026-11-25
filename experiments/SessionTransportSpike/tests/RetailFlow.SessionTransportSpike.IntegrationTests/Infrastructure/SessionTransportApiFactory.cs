using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

using RetailFlow.SessionTransportSpike.Api.Infrastructure.DataProtection;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

public sealed class SessionTransportApiFactory
    : WebApplicationFactory<SessionTransportProgramMarker>
{
    private readonly FakeTimeProvider _fakeTimeProvider;

    private readonly string _applicationName;
    private readonly string _certificatePath;
    private readonly string _certificatePassword;

    public FakeTimeProvider FakeTimeProvider =>
        _fakeTimeProvider;

    // The ONLY public constructor.
    // Required by xUnit IClassFixture<T>.
    public SessionTransportApiFactory()
        : this(
            RetailFlowDataProtectionDefaults.ApplicationName,
            TestDataProtectionCertificates.Shared.Path,
            TestDataProtectionCertificates.Shared.Password,
            new FakeTimeProvider(DateTimeOffset.UtcNow))
    {
    }

    // Private constructor for creating independent API instances.
    private SessionTransportApiFactory(
        string applicationName,
        string certificatePath,
        string certificatePassword,
        FakeTimeProvider fakeTimeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificatePassword);

        ArgumentNullException.ThrowIfNull(fakeTimeProvider);

        _applicationName = applicationName;
        _certificatePath = certificatePath;
        _certificatePassword = certificatePassword;

        _fakeTimeProvider = fakeTimeProvider;
    }

    // Factory method for independent instances.
    internal static SessionTransportApiFactory Create(
        string applicationName,
        string certificatePath,
        string certificatePassword,
        FakeTimeProvider fakeTimeProvider)
    {
        return new SessionTransportApiFactory(
            applicationName,
            certificatePath,
            certificatePassword,
            fakeTimeProvider);
    }

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable(
            "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
        ?? "Server=.;Database=RetailFlowIdentitySpike;" +
           "Trusted_Connection=True;Encrypt=True;" +
           "TrustServerCertificate=True;MultipleActiveResultSets=True";

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration =>
        {
            configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:IdentitySpike"] =
                        ConnectionString,

                    ["Security:BrowserCsrf:BrowserOrigin"] =
                        "https://localhost",

                    ["DataProtection:ApplicationName"] =
                        _applicationName,

                    ["DataProtection:Certificate:Path"] =
                        _certificatePath,

                    ["DataProtection:Certificate:Password"] =
                        _certificatePassword
                });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();

            services.AddSingleton<TimeProvider>(
                _fakeTimeProvider);
        });
    }
}
