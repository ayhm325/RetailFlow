using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

public sealed class SessionTransportApiFactory
    : WebApplicationFactory<SessionTransportProgramMarker>
{
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
    }
}
