namespace RetailFlow.Domain.Orders;

/// <summary>
/// Atomic Checkout Idempotency record per ADR-0006 (Accepted 2026-11-26).
/// </summary>
public sealed class CheckoutRequest
{
    private CheckoutRequest()
    {
        IdempotencyKey = string.Empty;
        RequestHash = string.Empty;
    }

    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string IdempotencyKey { get; private set; }
    public string RequestHash { get; private set; }
    public CheckoutRequestStatus Status { get; private set; }
    public Guid? OrderId { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public static CheckoutRequest Claim(
        Guid id,
        Guid storeId,
        Guid customerId,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAtUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("CheckoutRequest id cannot be empty.", nameof(id));
        if (storeId == Guid.Empty) throw new ArgumentException("StoreId cannot be empty.", nameof(storeId));
        if (customerId == Guid.Empty) throw new ArgumentException("CustomerId cannot be empty.", nameof(customerId));
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Trim().Length > 128)
        {
            throw new ArgumentException("IdempotencyKey must be 1..128 characters.", nameof(idempotencyKey));
        }

        if (string.IsNullOrWhiteSpace(requestHash) || requestHash.Length != 64)
        {
            throw new ArgumentException("RequestHash must be a 64-character SHA-256 hex string.", nameof(requestHash));
        }

        return new CheckoutRequest
        {
            Id = id,
            StoreId = storeId,
            CustomerId = customerId,
            IdempotencyKey = idempotencyKey.Trim(),
            RequestHash = requestHash,
            Status = CheckoutRequestStatus.InProgress,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = createdAtUtc.AddHours(24)
        };
    }

    public void MarkCompleted(Guid orderId, int responseStatusCode)
    {
        OrderId = orderId;
        ResponseStatusCode = responseStatusCode;
        Status = CheckoutRequestStatus.Completed;
    }

    public void MarkFailed(int responseStatusCode)
    {
        ResponseStatusCode = responseStatusCode;
        Status = CheckoutRequestStatus.Failed;
    }
}
