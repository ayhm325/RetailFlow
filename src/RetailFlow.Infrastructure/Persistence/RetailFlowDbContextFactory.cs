using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RetailFlow.Infrastructure.Persistence;

public sealed class RetailFlowDbContextFactory
    : IDesignTimeDbContextFactory<RetailFlowDbContext>
{
    public const string DefaultLocalConnectionString =
        "Server=.;Database=RetailFlowProduct;" +
        "Trusted_Connection=True;Encrypt=True;" +
        "TrustServerCertificate=True;MultipleActiveResultSets=True";

    public RetailFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("RETAILFLOW_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__RetailFlow")
            ?? DefaultLocalConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<RetailFlowDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new RetailFlowDbContext(optionsBuilder.Options);
    }
}
