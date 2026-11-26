using System.Data;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

internal interface IOrderReleaseHook
{
    Task OnBeforeSaveOrderReleaseAsync(Guid orderId, int attempt, CancellationToken cancellationToken);
}

public sealed class OrderService : IOrderService
{
    public const int MaxPageSize = 50;
    private const int ReservationSweepBatchSize = 50;
    private const int MaxOrderConcurrencyRetries = 3;

    private readonly RetailFlowDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrderService> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly IOrderReleaseHook? _releaseHook;

    public OrderService(
        RetailFlowDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderService>? logger = null,
        TimeProvider? timeProvider = null)
        : this(dbContext, scopeFactory, logger, timeProvider, null)
    {
    }

    internal OrderService(
        RetailFlowDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        ILogger<OrderService>? logger,
        TimeProvider? timeProvider,
        IOrderReleaseHook? releaseHook)
    {
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
        _logger = logger ?? NullLogger<OrderService>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _releaseHook = releaseHook;
    }

    public async Task<PagedOrdersResponse> GetCustomerOrdersAsync(
        Guid storeId,
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId && o.CustomerId == customerId);

        return await ExecutePagedOrderQueryAsync(baseQuery, page, pageSize, cancellationToken);
    }

    public async Task<OrderDto?> GetCustomerOrderByIdAsync(
        Guid storeId,
        Guid customerId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.CustomerId == customerId && o.Id == orderId, cancellationToken);

        return order is null ? null : Map(order);
    }

    public async Task<OrderDto?> CancelByCustomerAsync(
        Guid storeId,
        Guid customerId,
        Guid orderId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.CustomerId == customerId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.CancelByCustomer(customerId, reason, _timeProvider.GetUtcNow());
        await ReleaseOrderReservationsAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(order);
    }

    public async Task<PagedOrdersResponse> GetMerchantOrdersAsync(
        Guid storeId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId);

        return await ExecutePagedOrderQueryAsync(baseQuery, page, pageSize, cancellationToken);
    }

    public async Task<OrderDto?> ConfirmOrderAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.Confirm(actorUserId, "MerchantOwner", _timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderDto?> RejectOrderAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        string reason,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.Reject(actorUserId, "MerchantOwner", reason, _timeProvider.GetUtcNow());
        await ReleaseOrderReservationsAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(order);
    }

    public async Task<OrderDto?> AdvanceFulfillmentAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        FulfillmentStatus nextStatus,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.AdvanceFulfillment(actorUserId, "MerchantOwner", nextStatus, _timeProvider.GetUtcNow());
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(order);
    }

    public async Task<OrderDto?> CollectCodPaymentAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.CollectCodPayment(actorUserId, "MerchantOwner", _timeProvider.GetUtcNow());
        await CommitOrderReservationsAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(order);
    }

    public async Task<int> ReleaseExpiredReservationsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var releasedCount = 0;
        var skippedOrderIds = new HashSet<Guid>();

        while (!cancellationToken.IsCancellationRequested)
        {
            // Query only candidate Order IDs without tracking so each order executes in an isolated Unit of Work.
            var candidateIds = await _dbContext.Orders
                .AsNoTracking()
                .Where(o =>
                    o.OrderStatus == OrderStatus.Pending &&
                    o.ReservationExpiresAtUtc <= utcNow &&
                    !skippedOrderIds.Contains(o.Id))
                .OrderBy(o => o.ReservationExpiresAtUtc)
                .ThenBy(o => o.Id)
                .Select(o => o.Id)
                .Take(ReservationSweepBatchSize)
                .ToListAsync(cancellationToken);

            if (candidateIds.Count == 0)
            {
                break;
            }

            foreach (var orderId in candidateIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var outcome = await ReleaseSingleExpiredOrderAsync(orderId, utcNow, cancellationToken);
                if (outcome == OrderReleaseOutcome.Released)
                {
                    releasedCount++;
                }
                else if (outcome == OrderReleaseOutcome.Skipped)
                {
                    skippedOrderIds.Add(orderId);
                }
            }

            if (candidateIds.Count < ReservationSweepBatchSize)
            {
                break;
            }
        }

        return releasedCount;
    }

    private enum OrderReleaseOutcome
    {
        Released = 1,
        AlreadyProcessed = 2,
        Skipped = 3
    }

    private async Task<OrderReleaseOutcome> ReleaseSingleExpiredOrderAsync(
        Guid orderId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxOrderConcurrencyRetries; attempt++)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var unitDb = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

            Guid? storeId = null;
            try
            {
                await using var tx = await unitDb.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

                var order = await unitDb.Orders
                    .Include(o => o.Items)
                    .Include(o => o.History)
                    .AsSplitQuery()
                    .SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);

                if (order is null || order.OrderStatus != OrderStatus.Pending || order.ReservationExpiresAtUtc > utcNow)
                {
                    await tx.CommitAsync(cancellationToken);
                    return OrderReleaseOutcome.AlreadyProcessed;
                }

                storeId = order.StoreId;
                order.CancelByMerchantOrTimeout(null, "System", "Reservation expired (15-minute timeout)", utcNow);
                await ReleaseOrderReservationsAsync(unitDb, order, cancellationToken);

                if (_releaseHook is not null)
                {
                    await _releaseHook.OnBeforeSaveOrderReleaseAsync(order.Id, attempt, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                }

                await unitDb.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Expired order reservation released. StoreId={StoreId} OrderId={OrderId} Attempt={Attempt} Outcome={Outcome}",
                    storeId,
                    orderId,
                    attempt,
                    "Released");

                return OrderReleaseOutcome.Released;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Concurrency conflict releasing expired order. StoreId={StoreId} OrderId={OrderId} Attempt={Attempt} ExceptionType={ExceptionType} Outcome={Outcome}",
                    storeId,
                    orderId,
                    attempt,
                    ex.GetType().Name,
                    attempt < MaxOrderConcurrencyRetries ? "Retrying" : "ConcurrencyExhausted");

                if (attempt == MaxOrderConcurrencyRetries)
                {
                    return OrderReleaseOutcome.Skipped;
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                _logger.LogError(
                    ex,
                    "Domain/state error releasing expired order; skipping order. StoreId={StoreId} OrderId={OrderId} Attempt={Attempt} ExceptionType={ExceptionType} Outcome={Outcome}",
                    storeId,
                    orderId,
                    attempt,
                    ex.GetType().Name,
                    "Skipped");
                return OrderReleaseOutcome.Skipped;
            }
        }

        return OrderReleaseOutcome.Skipped;
    }

    private static async Task<PagedOrdersResponse> ExecutePagedOrderQueryAsync(
        IQueryable<Order> baseQuery,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        var skipLong = (long)(normalizedPage - 1) * normalizedPageSize;
        if (skipLong > int.MaxValue || skipLong >= totalCount)
        {
            return new PagedOrdersResponse(
                normalizedPage,
                normalizedPageSize,
                totalCount,
                totalPages,
                Array.Empty<OrderDto>());
        }

        var orders = await baseQuery
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((int)skipLong)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedOrdersResponse(
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages,
            orders.Select(Map).ToList());
    }

    private static async Task ReleaseOrderReservationsAsync(
        RetailFlowDbContext dbContext,
        Order order,
        CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var inventories = await dbContext.InventoryItems
            .Where(i => i.StoreId == order.StoreId && productIds.Contains(i.ProductId))
            .ToDictionaryAsync(i => i.ProductId, cancellationToken);

        foreach (var item in order.Items)
        {
            if (inventories.TryGetValue(item.ProductId, out var inv))
            {
                inv.ReleaseReservation(item.Quantity);
            }
        }
    }

    private Task ReleaseOrderReservationsAsync(Order order, CancellationToken cancellationToken) =>
        ReleaseOrderReservationsAsync(_dbContext, order, cancellationToken);

    private async Task CommitOrderReservationsAsync(Order order, CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var inventories = await _dbContext.InventoryItems
            .Where(i => i.StoreId == order.StoreId && productIds.Contains(i.ProductId))
            .ToDictionaryAsync(i => i.ProductId, cancellationToken);

        foreach (var item in order.Items)
        {
            if (inventories.TryGetValue(item.ProductId, out var inv))
            {
                inv.CommitReservation(item.Quantity);
            }
        }
    }

    internal static OrderDto Map(Order order) =>
        new(
            order.Id,
            order.StoreId,
            order.CustomerId,
            order.OrderNumber,
            order.FulfillmentMode,
            order.OrderStatus,
            order.FulfillmentStatus,
            order.PaymentStatus,
            order.CurrencyCode,
            order.Subtotal,
            order.DeliveryFee,
            order.Total,
            order.DeliveryZoneId,
            order.DeliveryZoneName,
            order.DeliveryAddress,
            order.CancellationReason,
            order.CreatedAtUtc,
            order.ReservationExpiresAtUtc,
            order.Items.Select(i => new OrderItemDto(
                i.ProductId,
                i.ProductName,
                i.Sku,
                i.UnitPrice,
                i.Quantity,
                i.LineTotal)).ToList());
}
