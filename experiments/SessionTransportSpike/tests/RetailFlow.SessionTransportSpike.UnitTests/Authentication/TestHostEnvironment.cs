using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace RetailFlow.SessionTransportSpike.UnitTests.Authentication;

internal sealed class TestHostEnvironment : IHostEnvironment
{
    public TestHostEnvironment(string environmentName)
    {
        EnvironmentName = environmentName;
    }

    public string EnvironmentName { get; set; }

    public string ApplicationName { get; set; } =
        "RetailFlow.SessionTransportSpike.UnitTests";

    public string ContentRootPath { get; set; } =
        AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } =
        new NullFileProvider();
}
