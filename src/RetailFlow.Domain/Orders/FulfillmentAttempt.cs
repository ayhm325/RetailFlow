namespace RetailFlow.Domain.Orders;

public sealed class FulfillmentAttempt
{
    private readonly List<FulfillmentAttemptItem> _items = [];

    private FulfillmentAttempt()
    {
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public int? AttemptNumber { get; private set; }
    public FulfillmentAttemptStatus Status { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset PreparedAtUtc { get; private set; }
    public DateTimeOffset? DispatchedAtUtc { get; private set; }
    public DateTimeOffset? DeliveredAtUtc { get; private set; }
    public DateTimeOffset? FailedAtUtc { get; private set; }
    public DateTimeOffset? CancelledAtUtc { get; private set; }
    public DeliveryFailureReasonCode? FailureReasonCode { get; private set; }
    public DeliveryFailureCategory? FailureCategory { get; private set; }
    public string? FailureNote { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }
    public string? RetryIdempotencyKey { get; private set; }
    public string? RetryRequestHash { get; private set; }

    public IReadOnlyCollection<FulfillmentAttemptItem> Items => _items;

    public static DeliveryFailureCategory ClassifyFailureReason(DeliveryFailureReasonCode reasonCode) =>
        reasonCode switch
        {
            DeliveryFailureReasonCode.CustomerUnavailable or
            DeliveryFailureReasonCode.NoAnswer or
            DeliveryFailureReasonCode.RescheduleRequested => DeliveryFailureCategory.Temporary,

            DeliveryFailureReasonCode.IncompleteAddress or
            DeliveryFailureReasonCode.WrongPhoneNumber or
            DeliveryFailureReasonCode.AccessRestricted or
            DeliveryFailureReasonCode.LegacyUnknown => DeliveryFailureCategory.RequiresCorrection,

            _ => DeliveryFailureCategory.NonRecoverable
        };

    internal static FulfillmentAttempt CreatePrepared(
        Guid id,
        Guid storeId,
        Guid orderId,
        IReadOnlyList<OrderItem> orderItems,
        DateTimeOffset preparedAtUtc,
        string? retryIdempotencyKey = null,
        string? retryRequestHash = null)
    {
        var attempt = new FulfillmentAttempt
        {
            Id = id,
            StoreId = storeId,
            OrderId = orderId,
            AttemptNumber = null,
            Status = FulfillmentAttemptStatus.Prepared,
            IsActive = true,
            PreparedAtUtc = preparedAtUtc,
            RetryIdempotencyKey = string.IsNullOrWhiteSpace(retryIdempotencyKey) ? null : retryIdempotencyKey.Trim(),
            RetryRequestHash = string.IsNullOrWhiteSpace(retryRequestHash) ? null : retryRequestHash.Trim()
        };

        foreach (var item in orderItems)
        {
            attempt._items.Add(FulfillmentAttemptItem.Create(
                Guid.NewGuid(),
                storeId,
                orderId,
                id,
                item.Id,
                item.ProductId,
                item.Quantity));
        }

        return attempt;
    }

    internal void MarkDispatched(int attemptNumber, DateTimeOffset dispatchedAtUtc)
    {
        if (Status != FulfillmentAttemptStatus.Prepared)
        {
            throw new OrderStateConflictException($"Cannot dispatch attempt from status '{Status}'.");
        }

        AttemptNumber = attemptNumber;
        Status = FulfillmentAttemptStatus.Dispatched;
        IsActive = true;
        DispatchedAtUtc = dispatchedAtUtc;
    }

    internal void MarkDelivered(DateTimeOffset deliveredAtUtc)
    {
        if (Status != FulfillmentAttemptStatus.Dispatched)
        {
            throw new OrderStateConflictException($"Cannot mark attempt delivered from status '{Status}'.");
        }

        Status = FulfillmentAttemptStatus.Delivered;
        IsActive = false;
        DeliveredAtUtc = deliveredAtUtc;
    }

    internal void MarkFailed(DeliveryFailureReasonCode reasonCode, string? note, DateTimeOffset failedAtUtc)
    {
        if (Status != FulfillmentAttemptStatus.Dispatched)
        {
            throw new OrderStateConflictException($"Cannot mark attempt failed from status '{Status}'.");
        }

        Status = FulfillmentAttemptStatus.Failed;
        IsActive = false;
        FailedAtUtc = failedAtUtc;
        FailureReasonCode = reasonCode;
        FailureCategory = ClassifyFailureReason(reasonCode);
        FailureNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

        foreach (var item in _items)
        {
            item.MarkPendingReturn();
        }
    }

    internal void MarkAbandoned()
    {
        if (Status != FulfillmentAttemptStatus.Prepared)
        {
            throw new OrderStateConflictException($"Cannot abandon attempt from status '{Status}'.");
        }

        Status = FulfillmentAttemptStatus.Abandoned;
        IsActive = false;
        AttemptNumber = null;
    }

    internal void MarkCancelled(DateTimeOffset cancelledAtUtc)
    {
        if (Status != FulfillmentAttemptStatus.Dispatched)
        {
            throw new OrderStateConflictException($"Cannot cancel dispatched attempt from status '{Status}'.");
        }

        Status = FulfillmentAttemptStatus.Cancelled;
        IsActive = false;
        CancelledAtUtc = cancelledAtUtc;

        foreach (var item in _items)
        {
            item.MarkPendingReturn();
        }
    }

    internal bool RecordCorrection(Guid actorUserId, string resolutionNote, DateTimeOffset resolvedAtUtc)
    {
        if (Status != FulfillmentAttemptStatus.Failed)
        {
            throw new OrderStateConflictException($"Cannot record correction on attempt with status '{Status}'.");
        }

        if (FailureCategory != DeliveryFailureCategory.RequiresCorrection)
        {
            throw new OrderStateConflictException($"Attempt failure category '{FailureCategory}' does not require correction.");
        }

        if (string.IsNullOrWhiteSpace(resolutionNote))
        {
            throw new ArgumentException("Resolution note is required.", nameof(resolutionNote));
        }

        var normalizedNote = resolutionNote.Trim();
        if (ResolvedAtUtc.HasValue)
        {
            if (string.Equals(ResolutionNote, normalizedNote, StringComparison.Ordinal))
            {
                return false;
            }

            throw new OrderStateConflictException("Delivery correction has already been resolved with a different note.");
        }

        ResolvedAtUtc = resolvedAtUtc;
        ResolvedByUserId = actorUserId;
        ResolutionNote = normalizedNote;
        return true;
    }
}

public sealed class FulfillmentAttemptItem
{
    private FulfillmentAttemptItem()
    {
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid FulfillmentAttemptId { get; private set; }
    public Guid OrderItemId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public ReturnDisposition ReturnDisposition { get; private set; }
    public DateTimeOffset? ReturnedAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public string? ResolutionNote { get; private set; }

    internal static FulfillmentAttemptItem Create(
        Guid id,
        Guid storeId,
        Guid orderId,
        Guid fulfillmentAttemptId,
        Guid orderItemId,
        Guid productId,
        int quantity)
    {
        return new FulfillmentAttemptItem
        {
            Id = id,
            StoreId = storeId,
            OrderId = orderId,
            FulfillmentAttemptId = fulfillmentAttemptId,
            OrderItemId = orderItemId,
            ProductId = productId,
            Quantity = quantity,
            ReturnDisposition = ReturnDisposition.NotRequired
        };
    }

    internal void MarkPendingReturn()
    {
        ReturnDisposition = ReturnDisposition.Pending;
        ReturnedAtUtc = null;
    }

    internal void ApplyDisposition(
        ReturnDisposition disposition,
        Guid actorUserId,
        string? note,
        DateTimeOffset occurredAtUtc)
    {
        if (disposition is not (ReturnDisposition.Restockable or ReturnDisposition.Damaged or ReturnDisposition.Lost))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition), "Disposition must be Restockable, Damaged, or Lost.");
        }

        if (ReturnDisposition != ReturnDisposition.Pending)
        {
            if (ReturnDisposition == disposition)
            {
                return;
            }

            throw new OrderStateConflictException(
                $"Attempt item already resolved as '{ReturnDisposition}'; cannot change to '{disposition}'.");
        }

        ReturnDisposition = disposition;
        ReturnedAtUtc = disposition is ReturnDisposition.Restockable or ReturnDisposition.Damaged
            ? occurredAtUtc
            : null;
        ResolvedAtUtc = occurredAtUtc;
        ResolvedByUserId = actorUserId;
        ResolutionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
    }
}
