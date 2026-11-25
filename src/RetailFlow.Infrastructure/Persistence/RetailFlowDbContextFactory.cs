using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RetailFlow.Infrastructure.Persistence;

public sealed class RetailFlowDbContextFactory
    : IDesignTimeDbContextFactory<RetailFlowDbContext>
{
    public RetailFlowDbContext CreateDbContext(string[] args)
    {
        var connectionString = ConnectionStringResolver.ResolveDesignTime();

        var optionsBuilder = new DbContextOptionsBuilder<RetailFlowDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new RetailFlowDbContext(optionsBuilder.Options);
    }
}
