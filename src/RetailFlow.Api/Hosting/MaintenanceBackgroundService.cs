using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Hosting;

/// <summary>
/// Periodic maintenance worker using PeriodicTimer + IServiceScopeFactory per Microsoft Learn guidance (GAP-002, GAP-003, GAP-005).
/// </summary>
public sealed class MaintenanceBackgroundService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MaintenanceBackgroundService> _logger;

    public MaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<MaintenanceBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunMaintenanceSweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Periodic maintenance sweep failed.");
            }
        }
    }

    public async Task RunMaintenanceSweepAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var orderService = scope.ServiceProvider.GetRequiredService<IOrderService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        var releasedOrders = await orderService.ReleaseExpiredReservationsAsync(now, cancellationToken);
        var deletedCheckoutRequests = await dbContext.CheckoutRequests
            .Where(r => r.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);
        var deletedTickets = await dbContext.SessionTickets
            .Where(t => t.ExpiresAtUtc <= now)
            .ExecuteDeleteAsync(cancellationToken);

        if (releasedOrders > 0 || deletedCheckoutRequests > 0 || deletedTickets > 0)
        {
            _logger.LogInformation(
                "Maintenance sweep completed: ReleasedOrders={ReleasedOrders}, DeletedCheckoutRequests={DeletedCheckoutRequests}, DeletedSessionTickets={DeletedTickets}",
                releasedOrders,
                deletedCheckoutRequests,
                deletedTickets);
        }
    }
}
