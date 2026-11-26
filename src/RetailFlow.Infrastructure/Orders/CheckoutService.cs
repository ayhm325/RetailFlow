using System.Data;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

internal interface ICheckoutFinalizationHook
{
    Task OnBeforeFinalizationAsync(Guid claimId, Guid claimToken, CancellationToken cancellationToken);
}

/// <summary>
/// Implements atomic Checkout Idempotency per ADR-0006 (Accepted) with Processing Lease recovery,
/// SQL-enforced stale-lease ownership fencing, Read-After-Error verification, and 15-minute inventory reservation per OD-001.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private readonly RetailFlowDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckoutService> _logger;
    private readonly ICheckoutFinalizationHook? _finalizationHook;

    public CheckoutService(
        RetailFlowDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        ILogger<CheckoutService> logger,
        TimeProvider? timeProvider = null)
        : this(dbContext, scopeFactory, logger, timeProvider, null)
    {
    }

    internal CheckoutService(
        RetailFlowDbContext dbContext,
        IServiceScopeFactory scopeFactory,
        ILogger<CheckoutService> logger,
        TimeProvider? timeProvider,
        ICheckoutFinalizationHook? finalizationHook)
    {
        _dbContext = dbContext;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _finalizationHook = finalizationHook;
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
        var now = _timeProvider.GetUtcNow();

        var cart = await _dbContext.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.StoreId == storeId && c.CustomerId == customerId, cancellationToken);

        var cartSignature = cart is null || cart.Items.Count == 0
            ? string.Empty
            : string.Join(",", cart.Items.OrderBy(i => i.ProductId).Select(i => $"{i.ProductId}:{i.Quantity}"));

        var requestHash = ComputeRequestHash(storeId, customerId, command, cartSignature);
        var claimToken = Guid.NewGuid();

        // Step 1: Atomic key claim or lease reclaim in CheckoutRequests
        var claim = CheckoutRequest.Claim(Guid.NewGuid(), storeId, customerId, normalizedKey, requestHash, now, claimToken);
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

            if (existing.ExpiresAtUtc <= now)
            {
                await _dbContext.CheckoutRequests
                    .Where(r => r.Id == existing.Id && r.ExpiresAtUtc <= now)
                    .ExecuteDeleteAsync(cancellationToken);

                return new CheckoutResult(
                    CheckoutOutcomeStatus.ValidationFailed,
                    null,
                    "Expired idempotency key was cleared; please retry.");
            }

            var effectiveHash = requestHash;
            OrderDto? completedOrder = null;
            if (string.IsNullOrEmpty(cartSignature) && existing.Status == CheckoutRequestStatus.Completed && existing.OrderId.HasValue)
            {
                completedOrder = await LoadOrderDtoAsync(_dbContext, storeId, existing.OrderId.Value, cancellationToken);
                if (completedOrder is not null)
                {
                    var orderCartSignature = string.Join(",", completedOrder.Items.OrderBy(i => i.ProductId).Select(i => $"{i.ProductId}:{i.Quantity}"));
                    effectiveHash = ComputeRequestHash(storeId, customerId, command, orderCartSignature);
                }
            }

            if (!string.Equals(existing.RequestHash, effectiveHash, StringComparison.Ordinal))
            {
                return new CheckoutResult(
                    CheckoutOutcomeStatus.PayloadMismatch,
                    null,
                    "Idempotency-Key was already used with a different checkout payload.");
            }

            if (existing.Status == CheckoutRequestStatus.Completed && existing.OrderId.HasValue)
            {
                var replayedOrder = completedOrder ?? await LoadOrderDtoAsync(_dbContext, storeId, existing.OrderId.Value, cancellationToken);
                if (replayedOrder is not null)
                {
                    return new CheckoutResult(CheckoutOutcomeStatus.Replayed, replayedOrder);
                }
            }

            if (existing.Status == CheckoutRequestStatus.InProgress && existing.LeaseExpiresAtUtc > now)
            {
                return new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrentInFlight,
                    null,
                    "A checkout request with this Idempotency-Key is currently in progress.");
            }

            // Stale InProgress lease expired OR previous Failed attempt: attempt atomic lease reclaim
            var reclaimedRows = await _dbContext.CheckoutRequests
                .Where(r =>
                    r.Id == existing.Id &&
                    r.ClaimToken == existing.ClaimToken &&
                    ((r.Status == CheckoutRequestStatus.InProgress && r.LeaseExpiresAtUtc <= now) ||
                     r.Status == CheckoutRequestStatus.Failed))
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(r => r.Status, CheckoutRequestStatus.InProgress)
                        .SetProperty(r => r.ClaimToken, claimToken)
                        .SetProperty(r => r.LastAttemptAtUtc, now)
                        .SetProperty(r => r.LeaseExpiresAtUtc, now.Add(CheckoutRequest.DefaultLeaseDuration)),
                    cancellationToken);

            if (reclaimedRows == 0)
            {
                return new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrentInFlight,
                    null,
                    "A checkout request with this Idempotency-Key is currently being recovered or completed.");
            }

            claim = (await _dbContext.CheckoutRequests
                .SingleAsync(r => r.Id == existing.Id && r.ClaimToken == claimToken, cancellationToken));
        }

        // Step 2: Execute single atomic SaveChangesAsync transaction (Order + OrderItems + InventoryItems + CartItems + Claim)
        try
        {
            if (cart is null || cart.Items.Count == 0)
            {
                await MarkClaimFailedIfOwnedAsync(claim.Id, claimToken, 400, cancellationToken);
                return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "Cart is empty.");
            }

            var store = await _dbContext.Stores
                .Include(s => s.Settings)
                .ThenInclude(ss => ss!.Currency)
                .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

            if (store?.Settings?.Currency is null || !store.IsPubliclyAccessible)
            {
                await MarkClaimFailedIfOwnedAsync(claim.Id, claimToken, 404, cancellationToken);
                return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "Store is not active.");
            }

            decimal deliveryFee = 0m;
            string? zoneName = null;
            if (command.FulfillmentMode == FulfillmentMode.Delivery)
            {
                if (!command.DeliveryZoneId.HasValue)
                {
                    await MarkClaimFailedIfOwnedAsync(claim.Id, claimToken, 400, cancellationToken);
                    return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "DeliveryZoneId is required.");
                }

                var zone = await _dbContext.DeliveryZones
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        z => z.Id == command.DeliveryZoneId.Value && z.StoreId == storeId && z.IsEnabled,
                        cancellationToken);

                if (zone is null)
                {
                    await MarkClaimFailedIfOwnedAsync(claim.Id, claimToken, 400, cancellationToken);
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
                    await MarkClaimFailedIfOwnedAsync(claim.Id, claimToken, 409, cancellationToken);
                    return new CheckoutResult(
                        CheckoutOutcomeStatus.ValidationFailed,
                        null,
                        "One or more items in your cart are unavailable. Please review your cart and retry.");
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

            // Detach the tracked claim so EF Core never issues an unfenced UPDATE CheckoutRequests WHERE Id = @id.
            _dbContext.Entry(claim).State = EntityState.Detached;

            if (_finalizationHook is not null)
            {
                await _finalizationHook.OnBeforeFinalizationAsync(claim.Id, claimToken, cancellationToken);
            }

            await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            // Database-enforced lease ownership fence inside the same transaction as Order/Inventory/Cart mutations.
            var completionNow = _timeProvider.GetUtcNow();
            var fencedRows = await _dbContext.CheckoutRequests
                .Where(r =>
                    r.Id == claim.Id &&
                    r.ClaimToken == claimToken &&
                    r.Status == CheckoutRequestStatus.InProgress &&
                    r.LeaseExpiresAtUtc > completionNow)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(r => r.OrderId, order.Id)
                        .SetProperty(r => r.ResponseStatusCode, 201)
                        .SetProperty(r => r.Status, CheckoutRequestStatus.Completed)
                        .SetProperty(r => r.CompletedAtUtc, completionNow),
                    cancellationToken);

            if (fencedRows == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var committed = await TryReadCompletedOrderInNewScopeAsync(storeId, claim.Id, cancellationToken);
                if (committed is not null)
                {
                    return new CheckoutResult(CheckoutOutcomeStatus.Replayed, committed);
                }

                return new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrentInFlight,
                    null,
                    "Checkout lease expired or was reclaimed by another request.");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            return new CheckoutResult(CheckoutOutcomeStatus.Created, OrderService.Map(order));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Do not swallow cancellation/shutdown; check independent DB state in case commit already succeeded.
            var committed = await TryReadCompletedOrderInNewScopeAsync(storeId, claim.Id, CancellationToken.None);
            if (committed is not null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.Replayed, committed);
            }

            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or DbUpdateConcurrencyException or DbUpdateException or SqlException)
        {
            _logger.LogWarning(
                ex,
                "Checkout failed for StoreId={StoreId} ClaimId={ClaimId}; verifying committed state before marking Failed.",
                storeId,
                claim.Id);

            // Clear all tracked mutations (e.g. partially reserved InventoryItems) before Read-After-Error.
            _dbContext.ChangeTracker.Clear();

            var committed = await TryReadCompletedOrderInNewScopeAsync(storeId, claim.Id, cancellationToken);
            if (committed is not null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.Replayed, committed);
            }

            await MarkClaimFailedIfOwnedAsync(claim.Id, claimToken, 409, cancellationToken);

            return new CheckoutResult(
                CheckoutOutcomeStatus.ValidationFailed,
                null,
                "Checkout could not be completed because item availability or order preconditions changed. Please review your cart and retry.");
        }
    }

    private async Task MarkClaimFailedIfOwnedAsync(
        Guid claimId,
        Guid claimToken,
        int statusCode,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        await _dbContext.CheckoutRequests
            .Where(r => r.Id == claimId && r.ClaimToken == claimToken && r.Status == CheckoutRequestStatus.InProgress)
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, CheckoutRequestStatus.Failed)
                    .SetProperty(r => r.ResponseStatusCode, statusCode),
                cancellationToken);
    }

    private async Task<OrderDto?> TryReadCompletedOrderInNewScopeAsync(
        Guid storeId,
        Guid claimId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

            var freshClaim = await freshDb.CheckoutRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == claimId, cancellationToken);

            if (freshClaim?.Status == CheckoutRequestStatus.Completed && freshClaim.OrderId.HasValue)
            {
                return await LoadOrderDtoAsync(freshDb, storeId, freshClaim.OrderId.Value, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read-After-Error failed for ClaimId={ClaimId}.", claimId);
        }

        return null;
    }

    private static async Task<OrderDto?> LoadOrderDtoAsync(
        RetailFlowDbContext db,
        Guid storeId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        return order is null ? null : OrderService.Map(order);
    }

    private static string ComputeRequestHash(
        Guid storeId,
        Guid customerId,
        CheckoutCommand command,
        string cartSignature)
    {
        var canonical = $"{storeId:N}|{customerId:N}|{(int)command.FulfillmentMode}|{command.DeliveryZoneId:N}|{command.DeliveryAddress?.Trim()}|{cartSignature}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627);
}
