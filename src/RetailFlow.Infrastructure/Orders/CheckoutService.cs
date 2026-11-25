using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

/// <summary>
/// Implements atomic Checkout Idempotency per ADR-0006 (Accepted) and 15-minute reservation per OD-001.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private readonly RetailFlowDbContext _dbContext;

    public CheckoutService(RetailFlowDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CheckoutResult> CheckoutAsync(
        Guid storeId,
        Guid customerId,
        string idempotencyKey,
        CheckoutCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 128)
        {
            return new CheckoutResult(
                CheckoutOutcomeStatus.ValidationFailed,
                null,
                "Idempotency-Key header is required and must be at most 128 characters.");
        }

        var normalizedKey = idempotencyKey.Trim();
        var now = DateTimeOffset.UtcNow;

        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

        var requestHash = ComputeRequestHash(storeId, customerId, command);

        // Step 1: Atomic key claim in CheckoutRequests
        var claim = CheckoutRequest.Claim(Guid.NewGuid(), storeId, customerId, normalizedKey, requestHash, now);
        _dbContext.CheckoutRequests.Add(claim);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            _dbContext.Entry(claim).State = EntityState.Detached;

            var existing = await _dbContext.CheckoutRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    r => r.StoreId == storeId && r.CustomerId == customerId && r.IdempotencyKey == normalizedKey,
                    cancellationToken);

            if (existing is null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.ConcurrentInFlight, null, "Duplicate checkout in flight.");
            }

            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
            {
                return new CheckoutResult(
                    CheckoutOutcomeStatus.PayloadMismatch,
                    null,
                    "Idempotency-Key was already used with a different checkout payload.");
            }

            if (existing.Status == CheckoutRequestStatus.InProgress)
            {
                return new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrentInFlight,
                    null,
                    "A checkout request with this Idempotency-Key is currently in progress.");
            }

            if (existing.Status == CheckoutRequestStatus.Completed && existing.OrderId.HasValue)
            {
                var replayedOrder = await LoadOrderDtoAsync(storeId, existing.OrderId.Value, cancellationToken);
                if (replayedOrder is not null)
                {
                    return new CheckoutResult(CheckoutOutcomeStatus.Replayed, replayedOrder);
                }
            }

            return new CheckoutResult(
                CheckoutOutcomeStatus.ValidationFailed,
                null,
                "Previous checkout attempt for this Idempotency-Key failed.");
        }

        // Step 2: Execute transactional checkout after atomic claim succeeded
        try
        {
            if (cart is null || cart.Items.Count == 0)
            {
                claim.MarkFailed(400);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "Cart is empty.");
            }

            var store = await _dbContext.Stores
                .Include(s => s.Settings)
                .ThenInclude(ss => ss!.Currency)
                .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

            if (store?.Settings?.Currency is null || !store.IsPubliclyAccessible)
            {
                claim.MarkFailed(404);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "Store is not active.");
            }

            decimal deliveryFee = 0m;
            string? zoneName = null;
            if (command.FulfillmentMode == FulfillmentMode.Delivery)
            {
                if (!command.DeliveryZoneId.HasValue)
                {
                    claim.MarkFailed(400);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "DeliveryZoneId is required.");
                }

                var zone = await _dbContext.DeliveryZones
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        z => z.Id == command.DeliveryZoneId.Value && z.StoreId == storeId && z.IsEnabled,
                        cancellationToken);

                if (zone is null)
                {
                    claim.MarkFailed(400);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "Selected delivery zone is unavailable.");
                }

                deliveryFee = zone.FlatFee;
                zoneName = zone.Name;
            }

            var productIds = cart.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _dbContext.Products
                .Include(p => p.InventoryItem)
                .Include(p => p.Category)
                .Where(p => p.StoreId == storeId && productIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);

            var orderId = Guid.NewGuid();
            var orderItems = new List<OrderItem>(cart.Items.Count);
            foreach (var cartItem in cart.Items)
            {
                if (!products.TryGetValue(cartItem.ProductId, out var product) ||
                    product.Status != ProductStatus.Published ||
                    product.Category is null ||
                    !product.Category.IsActive ||
                    product.InventoryItem is null)
                {
                    claim.MarkFailed(409);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return new CheckoutResult(
                        CheckoutOutcomeStatus.ValidationFailed,
                        null,
                        $"Product '{cartItem.ProductId}' is no longer available.");
                }

                product.InventoryItem.Reserve(cartItem.Quantity);
                orderItems.Add(OrderItem.Create(
                    Guid.NewGuid(),
                    storeId,
                    orderId,
                    product.Id,
                    product.Name,
                    product.Sku,
                    product.Price,
                    cartItem.Quantity,
                    store.Settings.Currency));
            }

            var orderNumber = $"RF-{now:yyyyMMdd}-{orderId.ToString("N")[..8].ToUpperInvariant()}";
            var order = Order.Create(
                orderId,
                store,
                store.Settings,
                store.Settings.Currency,
                customerId,
                orderNumber,
                command.FulfillmentMode,
                orderItems,
                deliveryFee,
                command.DeliveryZoneId,
                zoneName,
                command.DeliveryAddress,
                now);

            _dbContext.Orders.Add(order);
            _dbContext.CartItems.RemoveRange(cart.Items);
            claim.MarkCompleted(order.Id, 201);

            await _dbContext.SaveChangesAsync(cancellationToken);

            var dto = OrderService.Map(order);
            return new CheckoutResult(CheckoutOutcomeStatus.Created, dto);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateConcurrencyException)
        {
            claim.MarkFailed(409);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, ex.Message);
        }
    }

    private async Task<OrderDto?> LoadOrderDtoAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        return order is null ? null : OrderService.Map(order);
    }

    private static string ComputeRequestHash(
        Guid storeId,
        Guid customerId,
        CheckoutCommand command)
    {
        var canonical = $"{storeId:N}|{customerId:N}|{(int)command.FulfillmentMode}|{command.DeliveryZoneId:N}|{command.DeliveryAddress?.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
}
