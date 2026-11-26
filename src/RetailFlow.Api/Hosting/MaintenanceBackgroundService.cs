using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Periodic maintenance worker using PeriodicTimer + IServiceScopeFactory + TimeProvider per Microsoft Learn guidance.
/// Each maintenance task executes in an isolated async scope so one failure never skips or corrupts sibling tasks.
/// </summary>
public sealed class MaintenanceBackgroundService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MaintenanceBackgroundService> _logger;

    public MaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<MaintenanceBackgroundService> logger,
        TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval, _timeProvider);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunMaintenanceSweepAsync(stoppingToken);
        }
    }

    public async Task RunMaintenanceSweepAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();

        var releasedOrders = await RunIsolatedTaskAsync(
            "ReleaseExpiredReservations",
            async sp =>
            {
                var orderService = sp.GetRequiredService<IOrderService>();
                return await orderService.ReleaseExpiredReservationsAsync(now, cancellationToken);
            },
            cancellationToken);

        var deletedCheckoutRequests = await RunIsolatedTaskAsync(
            "DeleteExpiredCheckoutRequests",
            async sp =>
            {
                var db = sp.GetRequiredService<RetailFlowDbContext>();
                return await db.CheckoutRequests
                    .Where(r => r.ExpiresAtUtc <= now)
                    .ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

        var deletedTickets = await RunIsolatedTaskAsync(
            "DeleteExpiredSessionTickets",
            async sp =>
            {
                var db = sp.GetRequiredService<RetailFlowDbContext>();
                return await db.SessionTickets
                    .Where(t => t.ExpiresAtUtc <= now)
                    .ExecuteDeleteAsync(cancellationToken);
            },
            cancellationToken);

        if (releasedOrders > 0 || deletedCheckoutRequests > 0 || deletedTickets > 0)
        {
            _logger.LogInformation(
                "Maintenance sweep completed: ReleasedOrders={ReleasedOrders}, DeletedCheckoutRequests={DeletedCheckoutRequests}, DeletedSessionTickets={DeletedTickets}",
                releasedOrders,
                deletedCheckoutRequests,
                deletedTickets);
        }
    }

    private async Task<int> RunIsolatedTaskAsync(
        string taskName,
        Func<IServiceProvider, Task<int>> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            return await action(scope.ServiceProvider);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Maintenance task {TaskName} failed.", taskName);
            return 0;
        }
    }
}
