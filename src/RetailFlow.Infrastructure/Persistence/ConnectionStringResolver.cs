using Microsoft.Extensions.Configuration;

namespace RetailFlow.Infrastructure.Persistence;

internal static class ConnectionStringResolver
{
    internal const string DefaultLocalConnectionString =
        "Server=.;Database=RetailFlowProduct;" +
        "Trusted_Connection=True;Encrypt=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";

    internal static string ResolveRuntime(
        IConfiguration configuration,
        bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        // Read RETAILFLOW_CONNECTION through IConfiguration so test/host configuration
        // overrides take precedence over process-level environment variables.
        var primaryConnection = configuration["RETAILFLOW_CONNECTION"];
        if (!string.IsNullOrWhiteSpace(primaryConnection))
        {
            return primaryConnection;
        }

        var configConnection = configuration.GetConnectionString("RetailFlow");
        if (!string.IsNullOrWhiteSpace(configConnection))
        {
            return configConnection;
        }

        if (isDevelopment)
        {
            return DefaultLocalConnectionString;
        }

        throw new InvalidOperationException(
            "RetailFlow database connection string is not configured. " +
            "Set RETAILFLOW_CONNECTION or ConnectionStrings:RetailFlow.");
    }

    internal static string ResolveDesignTime()
    {
        var envConnection = Environment.GetEnvironmentVariable("RETAILFLOW_CONNECTION");
        if (!string.IsNullOrWhiteSpace(envConnection))
        {
            return envConnection;
        }

        var aspNetEnvConnection = Environment.GetEnvironmentVariable("ConnectionStrings__RetailFlow");
        if (!string.IsNullOrWhiteSpace(aspNetEnvConnection))
        {
            return aspNetEnvConnection;
        }

        return DefaultLocalConnectionString;
    }
}
