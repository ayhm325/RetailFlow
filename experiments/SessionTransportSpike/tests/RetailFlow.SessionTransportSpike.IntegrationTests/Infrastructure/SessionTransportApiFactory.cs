using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

public sealed class SessionTransportApiFactory
    : WebApplicationFactory<SessionTransportProgramMarker>
{
    private readonly FakeTimeProvider _fakeTimeProvider =
        new(DateTimeOffset.UtcNow);

    public FakeTimeProvider FakeTimeProvider => _fakeTimeProvider;

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
                        "https://localhost"
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
            // Replace the production system clock with one shared
            // controllable clock for Cookie Authentication and Ticket Store.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(
                _fakeTimeProvider);
        });
    }
}
