using Microsoft.Extensions.Diagnostics.HealthChecks;

using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Verifies SQL Server connectivity for the /health/ready endpoint.
/// </summary>
public sealed class DatabaseReadinessHealthCheck : IHealthCheck
{
    private readonly RetailFlowDbContext _dbContext;

    public DatabaseReadinessHealthCheck(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await _dbContext.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy("Database connection succeeded.")
                : HealthCheckResult.Unhealthy("Database readiness check failed.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HealthCheckResult.Unhealthy("Database readiness check failed.");
        }
    }
}
