using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

public sealed class SessionTicketDbContextFactory
    : IDesignTimeDbContextFactory<SessionTicketDbContext>
{
    public const string MigrationsHistoryTableName =
        "__EFMigrationsHistory_SessionTickets";

    public SessionTicketDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(
                "RETAILFLOW_IDENTITY_SPIKE_CONNECTION")
            ?? "Server=.;Database=RetailFlowIdentitySpike;" +
               "Trusted_Connection=True;Encrypt=True;" +
               "TrustServerCertificate=True;MultipleActiveResultSets=True";

        var optionsBuilder =
            new DbContextOptionsBuilder<SessionTicketDbContext>();

        optionsBuilder.UseSqlServer(
            connectionString,
            sqlOptions => sqlOptions.MigrationsHistoryTable(
                MigrationsHistoryTableName));

        return new SessionTicketDbContext(optionsBuilder.Options);
    }
}
