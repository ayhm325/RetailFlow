using System.Data;
using System.Security.Cryptography;
using System.Text.Json;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using RetailFlow.Application.Orders;
using RetailFlow.Domain.Inventory;
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
        await SaveOrderChangesAsync(storeId, orderId, nameof(CancelByCustomerAsync), cancellationToken);

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

    public async Task<OrderDto?> GetMerchantOrderByIdAsync(
        Guid storeId,
        Guid orderId,
        CancellationToken cancellationToken)
    {
        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Include(o => o.FulfillmentAttempts)
            .ThenInclude(a => a.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        return order is null ? null : Map(order);
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
            .Include(o => o.FulfillmentAttempts)
            .ThenInclude(a => a.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.Confirm(actorUserId, "MerchantOwner", _timeProvider.GetUtcNow());
        await SaveOrderChangesAsync(storeId, orderId, nameof(ConfirmOrderAsync), cancellationToken);
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
            .Include(o => o.FulfillmentAttempts)
            .ThenInclude(a => a.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.Reject(actorUserId, "MerchantOwner", reason, _timeProvider.GetUtcNow());
        await ReleaseOrderReservationsAsync(order, cancellationToken);
        await SaveOrderChangesAsync(storeId, orderId, nameof(RejectOrderAsync), cancellationToken);

        return Map(order);
    }

    public async Task<OrderDto?> AdvanceFulfillmentAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        FulfillmentStatus nextStatus,
        CancellationToken cancellationToken,
        DeliveryFailureReasonCode? failureReasonCode = null,
        string? failureNote = null)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .Include(o => o.FulfillmentAttempts)
            .ThenInclude(a => a.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        if (nextStatus == FulfillmentStatus.Preparing &&
            order.OrderStatus == OrderStatus.Confirmed &&
            order.FulfillmentStatus == FulfillmentStatus.Preparing &&
            (order.FulfillmentMode == FulfillmentMode.Pickup ||
             (order.FulfillmentAttempts.Count == 1 && order.FulfillmentAttempts.Single().Status == FulfillmentAttemptStatus.Prepared)))
        {
            return Map(order);
        }

        if (order.FulfillmentMode == FulfillmentMode.Delivery && nextStatus == FulfillmentStatus.Failed)
        {
            if (!failureReasonCode.HasValue || !Enum.IsDefined(failureReasonCode.Value))
            {
                throw new ArgumentException("A valid FailureReasonCode is required when marking delivery Failed.", nameof(failureReasonCode));
            }

            order.RecordDeliveryFailure(actorUserId, "MerchantOwner", failureReasonCode.Value, failureNote, _timeProvider.GetUtcNow());
        }
        else
        {
            order.AdvanceFulfillment(actorUserId, "MerchantOwner", nextStatus, _timeProvider.GetUtcNow());
        }

        await SaveOrderChangesAsync(storeId, orderId, nameof(AdvanceFulfillmentAsync), cancellationToken);
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
            .Include(o => o.FulfillmentAttempts)
            .ThenInclude(a => a.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        order.CollectCodPayment(actorUserId, "MerchantOwner", _timeProvider.GetUtcNow());
        await CommitOrderReservationsAsync(order, cancellationToken);
        await SaveOrderChangesAsync(storeId, orderId, nameof(CollectCodPaymentAsync), cancellationToken);

        return Map(order);
    }

    public async Task<OrderDto?> ResolveDeliveryCorrectionAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        string resolutionNote,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAggregateAsync(storeId, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        EnsureExpectedVersion(order, expectedVersion);
        order.ResolveDeliveryCorrection(actorUserId, "MerchantOwner", resolutionNote, _timeProvider.GetUtcNow());
        await SaveOrderChangesAsync(storeId, orderId, nameof(ResolveDeliveryCorrectionAsync), cancellationToken, requirePreconditionOnConcurrency: true);
        return Map(order);
    }

    public async Task<OrderDto?> RetryDeliveryAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        string idempotencyKey,
        string? note,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 128)
        {
            throw new ArgumentException("Idempotency-Key header is required and must be at most 128 characters.", nameof(idempotencyKey));
        }

        var normalizedKey = idempotencyKey.Trim();
        var requestHash = ComputeRetryRequestHash(storeId, orderId, note);

        var order = await LoadOrderAggregateAsync(storeId, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        // Replay lookup BEFORE If-Match evaluation (lost-response retry may carry old ETag)
        var existingAttempt = order.FulfillmentAttempts
            .SingleOrDefault(a => string.Equals(a.RetryIdempotencyKey, normalizedKey, StringComparison.Ordinal));
        if (existingAttempt is not null)
        {
            if (!string.Equals(existingAttempt.RetryRequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new OrderIdempotencyPayloadMismatchException(
                    "Idempotency-Key was already used with a different delivery retry payload.");
            }

            return Map(order);
        }

        EnsureExpectedVersion(order, expectedVersion);
        order.RetryDelivery(actorUserId, "MerchantOwner", _timeProvider.GetUtcNow(), normalizedKey, requestHash);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Map(order);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsRetryIdempotencyUniqueViolation(ex) || IsActiveAttemptUniqueViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();

            await using var scope = _scopeFactory.CreateAsyncScope();
            var freshDb = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();
            var freshOrder = await freshDb.Orders
                .AsNoTracking()
                .Include(o => o.Items)
                .Include(o => o.FulfillmentAttempts)
                .ThenInclude(a => a.Items)
                .AsSplitQuery()
                .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

            if (freshOrder is not null)
            {
                var winnerAttempt = freshOrder.FulfillmentAttempts
                    .SingleOrDefault(a => string.Equals(a.RetryIdempotencyKey, normalizedKey, StringComparison.Ordinal));
                if (winnerAttempt is not null)
                {
                    if (!string.Equals(winnerAttempt.RetryRequestHash, requestHash, StringComparison.Ordinal))
                    {
                        throw new OrderIdempotencyPayloadMismatchException(
                            "Idempotency-Key was already used with a different delivery retry payload.");
                    }

                    return Map(freshOrder);
                }
            }

            if (ex is DbUpdateConcurrencyException || IsActiveAttemptUniqueViolation(ex))
            {
                throw new OrderPreconditionException(
                    "The order version does not match the current server state. Refresh the order and retry.",
                    isMissingPrecondition: false,
                    ex);
            }

            throw new OrderConcurrencyConflictException(orderId, nameof(RetryDeliveryAsync), ex);
        }
    }

    public async Task<OrderDto?> CancelByMerchantAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        string reason,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Cancellation reason is required.", nameof(reason));
        }

        var normalizedReason = reason.Trim();
        var order = await LoadOrderAggregateAsync(storeId, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        // Replay lookup BEFORE If-Match evaluation
        if (order.OrderStatus == OrderStatus.Cancelled)
        {
            var cancelledByMerchant = order.History.Any(h =>
                h.ToOrderStatus == OrderStatus.Cancelled &&
                h.FromOrderStatus != OrderStatus.Cancelled &&
                string.Equals(h.ActorRole, "MerchantOwner", StringComparison.Ordinal));

            if (cancelledByMerchant && string.Equals(order.CancellationReason, normalizedReason, StringComparison.Ordinal))
            {
                return Map(order);
            }

            throw new OrderStateConflictException("Order is already cancelled with a different reason or actor.");
        }

        EnsureExpectedVersion(order, expectedVersion);

        var priorStatuses = order.Items.ToDictionary(i => i.Id, i => i.ReservationStatus);
        var now = _timeProvider.GetUtcNow();
        order.CancelByMerchantOrTimeout(actorUserId, "MerchantOwner", normalizedReason, now);

        var latestFailed = order.FulfillmentAttempts
            .Where(a => a.Status == FulfillmentAttemptStatus.Failed)
            .OrderByDescending(a => a.AttemptNumber ?? 0)
            .ThenByDescending(a => a.PreparedAtUtc)
            .FirstOrDefault();

        await ApplyOrderItemInventoryEffectsAsync(
            order,
            priorStatuses,
            latestFailed,
            actorUserId,
            now,
            cancellationToken);

        await SaveOrderChangesAsync(storeId, orderId, nameof(CancelByMerchantAsync), cancellationToken, requirePreconditionOnConcurrency: true);
        return Map(order);
    }

    public async Task<OrderDto?> ResolveInventoryDispositionAsync(
        Guid storeId,
        Guid actorUserId,
        Guid orderId,
        Guid fulfillmentAttemptId,
        IReadOnlyList<OrderItemDispositionCommand> items,
        string? expectedVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (items.Count == 0)
        {
            throw new ArgumentException("At least one item disposition is required.", nameof(items));
        }

        var order = await LoadOrderAggregateAsync(storeId, orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        var attempt = order.FulfillmentAttempts.SingleOrDefault(a => a.Id == fulfillmentAttemptId);
        if (attempt is null)
        {
            return null;
        }

        // Per-attempt replay check BEFORE If-Match evaluation
        var isReplay = true;
        foreach (var itemCmd in items)
        {
            if (itemCmd.Disposition is not (ReturnDisposition.Restockable or ReturnDisposition.Damaged or ReturnDisposition.Lost))
            {
                throw new ArgumentOutOfRangeException(nameof(items), "Disposition must be Restockable, Damaged, or Lost.");
            }

            var attemptItem = attempt.Items.SingleOrDefault(ai => ai.OrderItemId == itemCmd.OrderItemId)
                ?? throw new ArgumentException($"OrderItemId '{itemCmd.OrderItemId}' does not belong to FulfillmentAttempt '{fulfillmentAttemptId}'.", nameof(items));

            if (attemptItem.ReturnDisposition == ReturnDisposition.Pending)
            {
                isReplay = false;
            }
            else if (attemptItem.ReturnDisposition != itemCmd.Disposition)
            {
                throw new OrderStateConflictException(
                    $"Attempt item '{itemCmd.OrderItemId}' already resolved as '{attemptItem.ReturnDisposition}'; cannot change to '{itemCmd.Disposition}'.");
            }
        }

        if (isReplay)
        {
            return Map(order);
        }

        EnsureExpectedVersion(order, expectedVersion);

        var priorStatuses = order.Items.ToDictionary(i => i.Id, i => i.ReservationStatus);
        var now = _timeProvider.GetUtcNow();
        var domainDispositions = items
            .Select(i => (i.OrderItemId, i.Disposition, i.Note))
            .ToList();

        order.ResolveInventoryDisposition(actorUserId, "MerchantOwner", fulfillmentAttemptId, domainDispositions, now);

        await ApplyOrderItemInventoryEffectsAsync(
            order,
            priorStatuses,
            attempt,
            actorUserId,
            now,
            cancellationToken);

        await SaveOrderChangesAsync(storeId, orderId, nameof(ResolveInventoryDispositionAsync), cancellationToken, requirePreconditionOnConcurrency: true);
        return Map(order);
    }

    public async Task<int> ReleaseExpiredReservationsAsync(DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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
                cancellationToken.ThrowIfCancellationRequested();
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

    private async Task SaveOrderChangesAsync(
        Guid storeId,
        Guid orderId,
        string operation,
        CancellationToken cancellationToken,
        bool requirePreconditionOnConcurrency = false)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _dbContext.ChangeTracker.Clear();

            _logger.LogWarning(
                ex,
                "OrderConcurrencyConflict StoreId={StoreId} OrderId={OrderId} Operation={Operation}",
                storeId,
                orderId,
                operation);

            if (requirePreconditionOnConcurrency && ex.Entries.Any(e => e.Entity is Order))
            {
                throw new OrderPreconditionException(
                    "The order version does not match the current server state. Refresh the order and retry.",
                    isMissingPrecondition: false,
                    ex);
            }

            throw new OrderConcurrencyConflictException(
                orderId,
                operation,
                ex);
        }
        catch (DbUpdateException ex) when (IsOrderLifecycleUniqueRace(ex))
        {
            _dbContext.ChangeTracker.Clear();

            _logger.LogWarning(
                ex,
                "OrderConcurrencyConflict (UniqueIndexRace) StoreId={StoreId} OrderId={OrderId} Operation={Operation}",
                storeId,
                orderId,
                operation);

            if (requirePreconditionOnConcurrency)
            {
                throw new OrderPreconditionException(
                    "The order version does not match the current server state. Refresh the order and retry.",
                    isMissingPrecondition: false,
                    ex);
            }

            throw new OrderConcurrencyConflictException(orderId, operation, ex);
        }
    }

    private static bool IsOrderLifecycleUniqueRace(DbUpdateException ex) =>
        ex.InnerException is SqlException sqlEx &&
        sqlEx.Number is 2601 or 2627 &&
        (sqlEx.Message.Contains("UX_InventoryAdjustments_OrderItem_WriteOff", StringComparison.OrdinalIgnoreCase) ||
         sqlEx.Message.Contains("UX_FulfillmentAttempts_StoreId_OrderId_Active", StringComparison.OrdinalIgnoreCase) ||
         sqlEx.Message.Contains("UX_FulfillmentAttempts_StoreId_OrderId_AttemptNumber", StringComparison.OrdinalIgnoreCase));

    private Task<Order?> LoadOrderAggregateAsync(Guid storeId, Guid orderId, CancellationToken cancellationToken) =>
        _dbContext.Orders
            .Include(o => o.Items)
            .Include(o => o.History)
            .Include(o => o.FulfillmentAttempts)
            .ThenInclude(a => a.Items)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.StoreId == storeId && o.Id == orderId, cancellationToken);

    private static void EnsureExpectedVersion(Order order, string? expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            throw new OrderPreconditionException(
                "If-Match header is required for this operation.",
                isMissingPrecondition: true);
        }

        var currentVersion = EncodeVersion(order.RowVersion);
        if (!string.Equals(currentVersion, expectedVersion.Trim(), StringComparison.Ordinal))
        {
            throw new OrderPreconditionException(
                "The order version does not match the current server state. Refresh the order and retry.",
                isMissingPrecondition: false);
        }
    }

    private async Task ApplyOrderItemInventoryEffectsAsync(
        Order order,
        IReadOnlyDictionary<Guid, InventoryReservationStatus> priorStatuses,
        FulfillmentAttempt? sourceAttempt,
        Guid actorUserId,
        DateTimeOffset occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var changedItems = order.Items
            .Where(i => priorStatuses.TryGetValue(i.Id, out var prev) &&
                        prev != i.ReservationStatus &&
                        i.ReservationStatus is InventoryReservationStatus.Released or InventoryReservationStatus.WrittenOff)
            .ToList();

        if (changedItems.Count == 0)
        {
            return;
        }

        var productIds = changedItems.Select(i => i.ProductId).Distinct().ToList();
        var inventories = await _dbContext.InventoryItems
            .Where(i => i.StoreId == order.StoreId && productIds.Contains(i.ProductId))
            .ToDictionaryAsync(i => i.ProductId, cancellationToken);

        foreach (var item in changedItems)
        {
            if (!inventories.TryGetValue(item.ProductId, out var inv))
            {
                throw new InvalidOperationException($"InventoryItem missing for StoreId={order.StoreId}, ProductId={item.ProductId}.");
            }

            if (item.ReservationStatus == InventoryReservationStatus.Released)
            {
                inv.ReleaseReservation(item.Quantity);
            }
            else if (item.ReservationStatus == InventoryReservationStatus.WrittenOff)
            {
                inv.WriteOffReservation(item.Quantity);
                var attemptItem = sourceAttempt?.Items.SingleOrDefault(ai => ai.OrderItemId == item.Id);
                var opType = attemptItem?.ReturnDisposition == ReturnDisposition.Lost
                    ? InventoryAdjustmentOperationType.WriteOffLost
                    : InventoryAdjustmentOperationType.WriteOffDamaged;

                _dbContext.InventoryAdjustments.Add(InventoryAdjustment.CreateWriteOff(
                    Guid.NewGuid(),
                    order.StoreId,
                    item.ProductId,
                    order.Id,
                    item.Id,
                    attemptItem?.Id,
                    opType,
                    item.Quantity,
                    sourceAttempt?.FailureReasonCode,
                    actorUserId,
                    occurredAtUtc,
                    attemptItem?.ResolutionNote));
            }
        }
    }

    private static string EncodeVersion(byte[]? rowVersion) =>
        rowVersion is { Length: > 0 } ? Convert.ToBase64String(rowVersion) : string.Empty;

    private static string ComputeRetryRequestHash(Guid storeId, Guid orderId, string? note)
    {
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Operation = "order.delivery.retry.v1",
            StoreId = storeId,
            OrderId = orderId,
            Note = note?.Trim() ?? string.Empty
        });
        return Convert.ToHexString(SHA256.HashData(canonical));
    }

    private static bool IsRetryIdempotencyUniqueViolation(Exception ex) =>
        ex is DbUpdateException dbEx &&
        dbEx.InnerException is SqlException sqlEx &&
        sqlEx.Number is 2601 or 2627 &&
        sqlEx.Message.Contains("UX_FulfillmentAttempts_StoreId_OrderId_RetryIdempotencyKey", StringComparison.OrdinalIgnoreCase);

    private static bool IsActiveAttemptUniqueViolation(Exception ex) =>
        ex is DbUpdateException dbEx &&
        dbEx.InnerException is SqlException sqlEx &&
        sqlEx.Number is 2601 or 2627 &&
        sqlEx.Message.Contains("UX_FulfillmentAttempts_StoreId_OrderId_Active", StringComparison.OrdinalIgnoreCase);

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
                i.LineTotal,
                i.ReservationStatus,
                i.Id)).ToList(),
            order.ReservationSummary,
            EncodeVersion(order.RowVersion),
            order.FulfillmentAttempts
                .OrderBy(a => a.PreparedAtUtc)
                .Select(a => new FulfillmentAttemptDto(
                    a.Id,
                    a.AttemptNumber,
                    a.Status,
                    a.IsActive,
                    a.PreparedAtUtc,
                    a.DispatchedAtUtc,
                    a.DeliveredAtUtc,
                    a.FailedAtUtc,
                    a.CancelledAtUtc,
                    a.FailureReasonCode,
                    a.FailureCategory,
                    a.FailureNote,
                    a.ResolvedAtUtc,
                    a.ResolvedByUserId,
                    a.ResolutionNote,
                    a.Items.Select(ai => new FulfillmentAttemptItemDto(
                        ai.Id,
                        ai.OrderItemId,
                        ai.ProductId,
                        ai.Quantity,
                        ai.ReturnDisposition,
                        ai.ReturnedAtUtc,
                        ai.ResolvedAtUtc,
                        ai.ResolvedByUserId,
                        ai.ResolutionNote)).ToList()))
                .ToList());
}
