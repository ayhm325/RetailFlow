using System.Data;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Catalog;
using RetailFlow.Domain.Inventory;
using RetailFlow.Domain.Orders;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Infrastructure.Orders;

internal interface ICheckoutFinalizationHook
{
    Task OnBeforeFinalizationAsync(Guid claimId, Guid claimToken, CancellationToken cancellationToken);
}

/// <summary>
/// Implements atomic Checkout Idempotency per ADR-005 (Amended for AUD-004),
/// separating short Processing Lease (60s) from 24h Completed Retention,
/// Atomic Reclaim for expired generations, ExecutionClaim persisted identity continuity,
/// and bounded independent-context Commit reconciliation.
/// </summary>
public sealed class CheckoutService : ICheckoutService
{
    private const int MaxAcquisitionAttempts = 2;
    private static readonly TimeSpan ReconciliationTimeout = TimeSpan.FromSeconds(5);

    private readonly RetailFlowDbContext _dbContext;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CheckoutService> _logger;
    private readonly ICheckoutFinalizationHook? _finalizationHook;

    private readonly record struct ExecutionClaim(Guid ClaimId, Guid ClaimToken, string RequestHash);

    private enum ReconciliationStatus
    {
        Completed = 1,
        StillInProgress = 2,
        NotCompleted = 3,
        Inconclusive = 4
    }

    private readonly record struct ReconciliationResult(ReconciliationStatus Status, OrderDto? Order);

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

        // Step 1: Bounded idempotency key acquisition, replay classification, Lease recovery, or Atomic Reclaim
        var (acquiredClaim, immediateResult) = await AcquireOrClassifyAsync(
            storeId,
            customerId,
            normalizedKey,
            command,
            cartSignature,
            requestHash,
            now,
            cancellationToken);

        if (immediateResult is not null)
        {
            return immediateResult;
        }

        var activeClaim = acquiredClaim!.Value;

        // Step 2: Execute single atomic transaction (Fenced CheckoutRequest completion + Order + OrderItems + InventoryItems + CartItems)
        var commitAttempted = false;
        try
        {
            if (cart is null || cart.Items.Count == 0)
            {
                await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 422, cancellationToken);
                return new CheckoutResult(CheckoutOutcomeStatus.BusinessRuleFailed, null, "Cart is empty.");
            }

            var store = await _dbContext.Stores
                .Include(s => s.Settings)
                .ThenInclude(ss => ss!.Currency)
                .SingleOrDefaultAsync(s => s.Id == storeId, cancellationToken);

            if (store?.Settings?.Currency is null || !store.IsPubliclyAccessible)
            {
                await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 422, cancellationToken);
                return new CheckoutResult(CheckoutOutcomeStatus.BusinessRuleFailed, null, "Store is not active.");
            }

            decimal deliveryFee = 0m;
            string? zoneName = null;
            if (command.FulfillmentMode == FulfillmentMode.Delivery)
            {
                if (!command.DeliveryZoneId.HasValue)
                {
                    await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 400, cancellationToken);
                    return new CheckoutResult(CheckoutOutcomeStatus.ValidationFailed, null, "DeliveryZoneId is required.");
                }

                var zone = await _dbContext.DeliveryZones
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        z => z.Id == command.DeliveryZoneId.Value && z.StoreId == storeId && z.IsEnabled,
                        cancellationToken);

                if (zone is null)
                {
                    await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 422, cancellationToken);
                    return new CheckoutResult(CheckoutOutcomeStatus.BusinessRuleFailed, null, "Selected delivery zone is unavailable.");
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
                    await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 422, cancellationToken);
                    return new CheckoutResult(
                        CheckoutOutcomeStatus.BusinessRuleFailed,
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

            if (_finalizationHook is not null)
            {
                await _finalizationHook.OnBeforeFinalizationAsync(activeClaim.ClaimId, activeClaim.ClaimToken, cancellationToken);
            }

            await using var tx = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

            var completionNow = _timeProvider.GetUtcNow();
            var completedRetention = completionNow.Add(CheckoutRequest.DefaultRetentionDuration);
            var fencedRows = await _dbContext.CheckoutRequests
                .Where(r =>
                    r.Id == activeClaim.ClaimId &&
                    r.ClaimToken == activeClaim.ClaimToken &&
                    r.Status == CheckoutRequestStatus.InProgress &&
                    r.LeaseExpiresAtUtc > completionNow)
                .ExecuteUpdateAsync(
                    s => s
                        .SetProperty(r => r.OrderId, order.Id)
                        .SetProperty(r => r.ResponseStatusCode, 201)
                        .SetProperty(r => r.Status, CheckoutRequestStatus.Completed)
                        .SetProperty(r => r.CompletedAtUtc, completionNow)
                        .SetProperty(r => r.ExpiresAtUtc, completedRetention),
                    cancellationToken);

            if (fencedRows == 0)
            {
                await tx.RollbackAsync(cancellationToken);
                _dbContext.ChangeTracker.Clear();

                var reconciled = await ReconcileInNewScopeAsync(storeId, activeClaim.ClaimId);
                if (reconciled.Status == ReconciliationStatus.Completed && reconciled.Order is not null)
                {
                    return new CheckoutResult(CheckoutOutcomeStatus.Replayed, reconciled.Order);
                }

                return new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrentInFlight,
                    null,
                    "Checkout lease expired or was reclaimed by another request.");
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            commitAttempted = true;
            await tx.CommitAsync(cancellationToken);

            return new CheckoutResult(CheckoutOutcomeStatus.Created, OrderService.Map(order));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var reconciled = await ReconcileInNewScopeAsync(storeId, activeClaim.ClaimId);
            if (reconciled.Status == ReconciliationStatus.Completed && reconciled.Order is not null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.Replayed, reconciled.Order);
            }

            throw;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            var isExpectedBusinessConflict = ex.Entries.Count > 0 &&
                ex.Entries.All(e => e.Entity is InventoryItem or Cart or CartItem);

            _dbContext.ChangeTracker.Clear();

            var reconciled = await ReconcileInNewScopeAsync(storeId, activeClaim.ClaimId);
            if (reconciled.Status == ReconciliationStatus.Completed && reconciled.Order is not null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.Replayed, reconciled.Order);
            }

            if (commitAttempted && reconciled.Status is ReconciliationStatus.StillInProgress or ReconciliationStatus.Inconclusive)
            {
                if (reconciled.Status == ReconciliationStatus.StillInProgress)
                {
                    return new CheckoutResult(
                        CheckoutOutcomeStatus.ConcurrentInFlight,
                        null,
                        "Checkout outcome is pending verification. Please retry with the same Idempotency-Key.");
                }

                throw;
            }

            if (isExpectedBusinessConflict)
            {
                _logger.LogWarning(
                    ex,
                    "Checkout concurrency conflict for StoreId={StoreId} ClaimId={ClaimId}.",
                    storeId,
                    activeClaim.ClaimId);

                await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 409, cancellationToken);

                return new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrencyConflict,
                    null,
                    "Checkout could not be completed because item availability or cart state changed concurrently. Please review your cart and retry.");
            }

            _logger.LogError(
                ex,
                "Unexpected DbUpdateConcurrencyException during checkout for StoreId={StoreId} ClaimId={ClaimId}.",
                storeId,
                activeClaim.ClaimId);

            await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 500, CancellationToken.None);
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            _logger.LogWarning(
                ex,
                "Checkout business rule rejected for StoreId={StoreId} ClaimId={ClaimId}.",
                storeId,
                activeClaim.ClaimId);

            _dbContext.ChangeTracker.Clear();

            var reconciled = await ReconcileInNewScopeAsync(storeId, activeClaim.ClaimId);
            if (reconciled.Status == ReconciliationStatus.Completed && reconciled.Order is not null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.Replayed, reconciled.Order);
            }

            if (commitAttempted &&
                (reconciled.Status == ReconciliationStatus.StillInProgress ||
                 reconciled.Status == ReconciliationStatus.Inconclusive))
            {
                if (reconciled.Status == ReconciliationStatus.StillInProgress)
                {
                    return new CheckoutResult(
                        CheckoutOutcomeStatus.ConcurrentInFlight,
                        null,
                        "Checkout outcome is pending verification. Please retry with the same Idempotency-Key.");
                }

                throw;
            }

            await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 422, cancellationToken);

            return new CheckoutResult(
                CheckoutOutcomeStatus.BusinessRuleFailed,
                null,
                "Checkout could not be completed because order preconditions or item availability were not satisfied.");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected persistence or system failure during checkout for StoreId={StoreId} ClaimId={ClaimId}.",
                storeId,
                activeClaim.ClaimId);

            _dbContext.ChangeTracker.Clear();

            var reconciled = await ReconcileInNewScopeAsync(storeId, activeClaim.ClaimId);
            if (reconciled.Status == ReconciliationStatus.Completed && reconciled.Order is not null)
            {
                return new CheckoutResult(CheckoutOutcomeStatus.Replayed, reconciled.Order);
            }

            if (commitAttempted && reconciled.Status is ReconciliationStatus.StillInProgress or ReconciliationStatus.Inconclusive)
            {
                if (reconciled.Status == ReconciliationStatus.StillInProgress)
                {
                    return new CheckoutResult(
                        CheckoutOutcomeStatus.ConcurrentInFlight,
                        null,
                        "Checkout outcome is pending verification. Please retry with the same Idempotency-Key.");
                }

                throw;
            }

            await TryMarkClaimFailedIfOwnedAsync(activeClaim.ClaimId, activeClaim.ClaimToken, 500, CancellationToken.None);
            throw;
        }
    }

    private async Task<(ExecutionClaim? Claim, CheckoutResult? Result)> AcquireOrClassifyAsync(
        Guid storeId,
        Guid customerId,
        string normalizedKey,
        CheckoutCommand command,
        string cartSignature,
        string requestHash,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAcquisitionAttempts; attempt++)
        {
            var claimToken = Guid.NewGuid();
            var pendingClaim = CheckoutRequest.Claim(Guid.NewGuid(), storeId, customerId, normalizedKey, requestHash, now, claimToken);
            _dbContext.CheckoutRequests.Add(pendingClaim);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                _dbContext.Entry(pendingClaim).State = EntityState.Detached;
                return (new ExecutionClaim(pendingClaim.Id, claimToken, requestHash), null);
            }
            catch (DbUpdateException ex) when (IsCheckoutRequestUniqueViolation(ex, pendingClaim))
            {
                _dbContext.Entry(pendingClaim).State = EntityState.Detached;

                var existing = await _dbContext.CheckoutRequests
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        r => r.StoreId == storeId && r.CustomerId == customerId && r.IdempotencyKey == normalizedKey,
                        cancellationToken);

                if (existing is null)
                {
                    if (attempt < MaxAcquisitionAttempts)
                    {
                        continue;
                    }

                    return (null, new CheckoutResult(CheckoutOutcomeStatus.ConcurrentInFlight, null, "Duplicate checkout in flight."));
                }

                // Expired Retention with no active Lease -> Atomic Reclaim as a new logical generation
                if (existing.ExpiresAtUtc <= now &&
                    (existing.Status != CheckoutRequestStatus.InProgress || existing.LeaseExpiresAtUtc <= now))
                {
                    var newLease = now.Add(CheckoutRequest.DefaultLeaseDuration);
                    var newRetention = now.Add(CheckoutRequest.DefaultRetentionDuration);

                    var reclaimedRows = await _dbContext.CheckoutRequests
                        .Where(r =>
                            r.Id == existing.Id &&
                            r.ClaimToken == existing.ClaimToken &&
                            r.ExpiresAtUtc <= now &&
                            (r.Status != CheckoutRequestStatus.InProgress || r.LeaseExpiresAtUtc <= now))
                        .ExecuteUpdateAsync(
                            s => s
                                .SetProperty(r => r.RequestHash, requestHash)
                                .SetProperty(r => r.Status, CheckoutRequestStatus.InProgress)
                                .SetProperty(r => r.ClaimToken, claimToken)
                                .SetProperty(r => r.OrderId, (Guid?)null)
                                .SetProperty(r => r.ResponseStatusCode, (int?)null)
                                .SetProperty(r => r.CompletedAtUtc, (DateTimeOffset?)null)
                                .SetProperty(r => r.LastAttemptAtUtc, now)
                                .SetProperty(r => r.LeaseExpiresAtUtc, newLease)
                                .SetProperty(r => r.ExpiresAtUtc, newRetention),
                            cancellationToken);

                    if (reclaimedRows == 1)
                    {
                        return (new ExecutionClaim(existing.Id, claimToken, requestHash), null);
                    }

                    var currentAfterReclaimRace = await _dbContext.CheckoutRequests
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            r => r.StoreId == storeId && r.CustomerId == customerId && r.IdempotencyKey == normalizedKey,
                            cancellationToken);

                    if (currentAfterReclaimRace is null)
                    {
                        if (attempt < MaxAcquisitionAttempts)
                        {
                            continue;
                        }

                        return (null, new CheckoutResult(CheckoutOutcomeStatus.ConcurrentInFlight, null, "Duplicate checkout in flight."));
                    }

                    existing = currentAfterReclaimRace;
                }

                // Active generation (or lost reclaim race): classify existing row
                var (classifiedClaim, classifiedResult) = await ClassifyOrRecoverExistingAsync(
                    existing,
                    storeId,
                    customerId,
                    normalizedKey,
                    command,
                    cartSignature,
                    requestHash,
                    claimToken,
                    now,
                    cancellationToken);

                if (classifiedClaim is not null || classifiedResult is not null)
                {
                    return (classifiedClaim, classifiedResult);
                }

                // Row was deleted concurrently during recovery race -> retry acquisition if attempts remain
                if (attempt < MaxAcquisitionAttempts)
                {
                    continue;
                }

                return (null, new CheckoutResult(
                    CheckoutOutcomeStatus.ConcurrentInFlight,
                    null,
                    "A checkout request with this Idempotency-Key is currently being recovered or completed."));
            }
        }

        return (null, new CheckoutResult(CheckoutOutcomeStatus.ConcurrentInFlight, null, "Duplicate checkout in flight."));
    }

    private async Task<(ExecutionClaim? Claim, CheckoutResult? Result)> ClassifyOrRecoverExistingAsync(
        CheckoutRequest existing,
        Guid storeId,
        Guid customerId,
        string normalizedKey,
        CheckoutCommand command,
        string cartSignature,
        string requestHash,
        Guid newClaimToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
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
            return (null, new CheckoutResult(
                CheckoutOutcomeStatus.PayloadMismatch,
                null,
                "Idempotency-Key was already used with a different checkout payload."));
        }

        if (existing.Status == CheckoutRequestStatus.Completed && existing.OrderId.HasValue)
        {
            var replayedOrder = completedOrder ?? await LoadOrderDtoAsync(_dbContext, storeId, existing.OrderId.Value, cancellationToken);
            if (replayedOrder is not null)
            {
                return (null, new CheckoutResult(CheckoutOutcomeStatus.Replayed, replayedOrder));
            }
        }

        if (existing.Status == CheckoutRequestStatus.InProgress && existing.LeaseExpiresAtUtc > now)
        {
            return (null, new CheckoutResult(
                CheckoutOutcomeStatus.ConcurrentInFlight,
                null,
                "A checkout request with this Idempotency-Key is currently in progress."));
        }

        // Stale InProgress lease expired OR previous Failed attempt within retention: attempt conditional Lease Recovery
        var newLease = now.Add(CheckoutRequest.DefaultLeaseDuration);
        var minProtectedRetention = newLease.Add(CheckoutRequest.LeaseRecoveryRetentionGrace);
        var recoveredRetention = existing.ExpiresAtUtc >= minProtectedRetention ? existing.ExpiresAtUtc : minProtectedRetention;

        var recoveredRows = await _dbContext.CheckoutRequests
            .Where(r =>
                r.Id == existing.Id &&
                r.ClaimToken == existing.ClaimToken &&
                ((r.Status == CheckoutRequestStatus.InProgress && r.LeaseExpiresAtUtc <= now) ||
                 r.Status == CheckoutRequestStatus.Failed))
            .ExecuteUpdateAsync(
                s => s
                    .SetProperty(r => r.Status, CheckoutRequestStatus.InProgress)
                    .SetProperty(r => r.ClaimToken, newClaimToken)
                    .SetProperty(r => r.LastAttemptAtUtc, now)
                    .SetProperty(r => r.LeaseExpiresAtUtc, newLease)
                    .SetProperty(r => r.ExpiresAtUtc, recoveredRetention),
                cancellationToken);

        if (recoveredRows == 1)
        {
            return (new ExecutionClaim(existing.Id, newClaimToken, requestHash), null);
        }

        // Zero-row recovery race: re-read current state and classify (may be Replay if winner just completed)
        var afterRace = await _dbContext.CheckoutRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                r => r.StoreId == storeId && r.CustomerId == customerId && r.IdempotencyKey == normalizedKey,
                cancellationToken);

        if (afterRace is null)
        {
            return (null, null); // Deleted by cleanup; caller may retry acquisition
        }

        if (afterRace.Status == CheckoutRequestStatus.Completed && afterRace.OrderId.HasValue)
        {
            var winnerOrder = await LoadOrderDtoAsync(_dbContext, storeId, afterRace.OrderId.Value, cancellationToken);
            if (winnerOrder is not null)
            {
                var winnerHash = string.IsNullOrEmpty(cartSignature)
                    ? ComputeRequestHash(storeId, customerId, command, string.Join(",", winnerOrder.Items.OrderBy(i => i.ProductId).Select(i => $"{i.ProductId}:{i.Quantity}")))
                    : requestHash;

                if (!string.Equals(afterRace.RequestHash, winnerHash, StringComparison.Ordinal))
                {
                    return (null, new CheckoutResult(
                        CheckoutOutcomeStatus.PayloadMismatch,
                        null,
                        "Idempotency-Key was already used with a different checkout payload."));
                }

                return (null, new CheckoutResult(CheckoutOutcomeStatus.Replayed, winnerOrder));
            }
        }

        return (null, new CheckoutResult(
            CheckoutOutcomeStatus.ConcurrentInFlight,
            null,
            "A checkout request with this Idempotency-Key is currently being recovered or completed."));
    }

    private async Task TryMarkClaimFailedIfOwnedAsync(
        Guid claimId,
        Guid claimToken,
        int statusCode,
        CancellationToken cancellationToken)
    {
        try
        {
            await MarkClaimFailedIfOwnedAsync(claimId, claimToken, statusCode, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort claim failure update failed for ClaimId={ClaimId}.", claimId);
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

    private async Task<ReconciliationResult> ReconcileInNewScopeAsync(
        Guid storeId,
        Guid claimId)
    {
        try
        {
            using var cts = new CancellationTokenSource(ReconciliationTimeout);
            await using var scope = _scopeFactory.CreateAsyncScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

            var freshClaim = await freshDb.CheckoutRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(r => r.Id == claimId, cts.Token);

            if (freshClaim is null)
            {
                return new ReconciliationResult(ReconciliationStatus.NotCompleted, null);
            }

            if (freshClaim.Status == CheckoutRequestStatus.Completed && freshClaim.OrderId.HasValue)
            {
                var order = await LoadOrderDtoAsync(freshDb, storeId, freshClaim.OrderId.Value, cts.Token);
                if (order is not null)
                {
                    return new ReconciliationResult(ReconciliationStatus.Completed, order);
                }
            }

            if (freshClaim.Status == CheckoutRequestStatus.InProgress)
            {
                return new ReconciliationResult(ReconciliationStatus.StillInProgress, null);
            }

            return new ReconciliationResult(ReconciliationStatus.NotCompleted, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Read-After-Error reconciliation failed for ClaimId={ClaimId}.", claimId);
            return new ReconciliationResult(ReconciliationStatus.Inconclusive, null);
        }
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

    private static bool IsCheckoutRequestUniqueViolation(DbUpdateException ex, CheckoutRequest pendingClaim)
    {
        if (ex.InnerException is not SqlException sqlEx || sqlEx.Number is not (2601 or 2627))
        {
            return false;
        }

        if (ex.Entries.Count != 1 || !ReferenceEquals(ex.Entries[0].Entity, pendingClaim))
        {
            return false;
        }

        return sqlEx.Message.Contains(
            "UX_CheckoutRequests_StoreId_CustomerId_IdempotencyKey",
            StringComparison.OrdinalIgnoreCase);
    }
}
