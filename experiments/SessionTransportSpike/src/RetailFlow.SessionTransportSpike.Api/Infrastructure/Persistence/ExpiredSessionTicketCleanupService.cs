using Microsoft.EntityFrameworkCore;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

/// <summary>
/// Periodically removes expired authentication session tickets.
///
/// Expiration cleanup is independent from Logout:
/// Logout removes a specific session immediately,
/// while this worker removes sessions whose expiration time has passed.
/// </summary>
public sealed class ExpiredSessionTicketCleanupService : BackgroundService
{
    private static readonly TimeSpan CleanupInterval =
        TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<SessionTicketDbContext>
        _dbContextFactory;

    private readonly TimeProvider _timeProvider;

    private readonly ILogger<ExpiredSessionTicketCleanupService>
        _logger;

    public ExpiredSessionTicketCleanupService(
        IDbContextFactory<SessionTicketDbContext> dbContextFactory,
        TimeProvider timeProvider,
        ILogger<ExpiredSessionTicketCleanupService> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _dbContextFactory = dbContextFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Expired session ticket cleanup service started. Interval: {Interval}.",
            CleanupInterval);

        // Perform one cleanup when the application starts.
        await ExecuteCleanupSafelyAsync(stoppingToken);

        using var timer = new PeriodicTimer(
            CleanupInterval,
            _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ExecuteCleanupSafelyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
            when (stoppingToken.IsCancellationRequested)
        {
            // Normal application shutdown.
        }

        _logger.LogInformation(
            "Expired session ticket cleanup service stopped.");
    }

    /// <summary>
    /// Deletes all expired authentication tickets.
    ///
    /// Public visibility allows integration tests to execute
    /// one cleanup cycle deterministically.
    /// </summary>
    public async Task<int> CleanupExpiredTicketsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var deletedCount = await dbContext.AuthenticationTickets
            .Where(ticket =>
                ticket.ExpiresUtc <= now ||
                ticket.AbsoluteExpiresUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Removed {DeletedCount} expired authentication session tickets.",
                deletedCount);
        }

        return deletedCount;
    }

    private async Task ExecuteCleanupSafelyAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var deletedCount =
                await CleanupExpiredTicketsAsync(cancellationToken);

            _logger.LogDebug(
                "Expired session cleanup completed. Deleted: {DeletedCount}.",
                deletedCount);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // A temporary database failure must not silently
            // terminate the background cleanup loop.
            _logger.LogError(
                exception,
                "Expired session cleanup failed. It will be retried on the next interval.");
        }
    }
}
