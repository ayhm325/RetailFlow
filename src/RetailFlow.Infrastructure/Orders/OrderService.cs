using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

public sealed class OrderService : IOrderService
{
    private readonly RetailFlowDbContext _dbContext;

    public OrderService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PagedOrdersResponse> GetCustomerOrdersAsync(
        Guid storeId,
        Guid customerId,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);

        var baseQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId && o.CustomerId == customerId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        var orders = await baseQuery
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedOrdersResponse(
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages,
            orders.Select(Map).ToList());
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

        order.CancelByCustomer(customerId, reason, DateTimeOffset.UtcNow);
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
        var normalizedPage = Math.Max(1, page);
        var normalizedPageSize = Math.Clamp(pageSize, 1, 50);

        var baseQuery = _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.StoreId == storeId);

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)normalizedPageSize);

        var orders = await baseQuery
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Skip((normalizedPage - 1) * normalizedPageSize)
            .Take(normalizedPageSize)
            .ToListAsync(cancellationToken);

        return new PagedOrdersResponse(
            normalizedPage,
            normalizedPageSize,
            totalCount,
            totalPages,
            orders.Select(Map).ToList());
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

        order.Confirm(actorUserId, "MerchantOwner", DateTimeOffset.UtcNow);
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

        order.Reject(actorUserId, "MerchantOwner", reason, DateTimeOffset.UtcNow);
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

        order.AdvanceFulfillment(actorUserId, "MerchantOwner", nextStatus, DateTimeOffset.UtcNow);
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

        order.CollectCodPayment(actorUserId, "MerchantOwner", DateTimeOffset.UtcNow);
        await CommitOrderReservationsAsync(order, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Map(order);
    }

    public async Task<int> ReleaseExpiredReservationsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var expiredOrderIds = await _dbContext.Orders
            .AsNoTracking()
            .Where(o => o.OrderStatus == OrderStatus.Pending && o.ReservationExpiresAtUtc <= utcNow)
            .Select(o => o.Id)
            .ToListAsync(cancellationToken);

        var releasedCount = 0;
        foreach (var orderId in expiredOrderIds)
        {
            _dbContext.ChangeTracker.Clear();

            var order = await _dbContext.Orders
                .Include(o => o.Items)
                .Include(o => o.History)
                .AsSplitQuery()
                .SingleOrDefaultAsync(o => o.Id == orderId && o.OrderStatus == OrderStatus.Pending, cancellationToken);

            if (order is null)
            {
                continue;
            }

            try
            {
                order.CancelByMerchantOrTimeout(null, "System", "Reservation expired (15-minute timeout)", utcNow);
                await ReleaseOrderReservationsAsync(order, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                releasedCount++;
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another worker instance concurrently updated/released this order; safe to skip.
                _dbContext.ChangeTracker.Clear();
            }
        }

        return releasedCount;
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
