using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RetailFlow.IdentitySpike.IdentitySqlServerIntegrationTests.Infrastructure.Identity;

public sealed class IdentitySpikeApiFactory
    : WebApplicationFactory<Program>
{
    private readonly Action<IServiceCollection>?
        _configureTestServices;

    public IdentitySpikeApiFactory()
        : this(null)
    {
    }

    internal IdentitySpikeApiFactory(
        Action<IServiceCollection>? configureTestServices)
    {
        _configureTestServices = configureTestServices;
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
                        ConnectionString
                });
        });

        return base.CreateHost(builder);
    }

    protected override void ConfigureWebHost(
        IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        if (_configureTestServices is not null)
        {
            builder.ConfigureTestServices(_configureTestServices);
        }
    }
}
