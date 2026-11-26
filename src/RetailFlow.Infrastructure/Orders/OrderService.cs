using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

public sealed class OrderService : IOrderService
{
    public const int MaxPageSize = 50;
    private const int ReservationSweepBatchSize = 50;

    private readonly RetailFlowDbContext _dbContext;
    private readonly TimeProvider _timeProvider;

    public OrderService(RetailFlowDbContext dbContext, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
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

        while (!cancellationToken.IsCancellationRequested)
        {
            _dbContext.ChangeTracker.Clear();

            var batch = await _dbContext.Orders
                .Include(o => o.Items)
                .Include(o => o.History)
                .AsSplitQuery()
                .Where(o => o.OrderStatus == OrderStatus.Pending && o.ReservationExpiresAtUtc <= utcNow)
                .OrderBy(o => o.ReservationExpiresAtUtc)
                .ThenBy(o => o.Id)
                .Take(ReservationSweepBatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
            {
                break;
            }

            var batchHadConflict = false;
            foreach (var order in batch)
            {
                try
                {
                    order.CancelByMerchantOrTimeout(null, "System", "Reservation expired (15-minute timeout)", utcNow);
                    await ReleaseOrderReservationsAsync(order, cancellationToken);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    releasedCount++;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Another worker instance concurrently updated/released this order; clear tracker and skip.
                    batchHadConflict = true;
                    _dbContext.ChangeTracker.Clear();
                }
            }

            if (batch.Count < ReservationSweepBatchSize && !batchHadConflict)
            {
                break;
            }
        }

        return releasedCount;
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

    private async Task ReleaseOrderReservationsAsync(Order order, CancellationToken cancellationToken)
    {
        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var inventories = await _dbContext.InventoryItems
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
