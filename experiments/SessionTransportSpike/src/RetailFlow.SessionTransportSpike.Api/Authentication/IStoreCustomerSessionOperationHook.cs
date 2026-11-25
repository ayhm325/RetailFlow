namespace RetailFlow.SessionTransportSpike.Api.Authentication;

/// <summary>
/// Optional coordination hook used by integration tests
/// to exercise session creation and password-change concurrency.
/// </summary>
public interface IStoreCustomerSessionOperationHook
{
    Task OnBeforeTicketStoreTransactionAsync(
        string userId,
        CancellationToken cancellationToken);

    Task OnTicketStoreUserRowLockedAsync(
        string userId,
        CancellationToken cancellationToken);

    Task OnBeforePasswordChangeUserRowLockAsync(
        string userId,
        CancellationToken cancellationToken);
}

public sealed class NoOpStoreCustomerSessionOperationHook
    : IStoreCustomerSessionOperationHook
{
    public static NoOpStoreCustomerSessionOperationHook Instance { get; } =
        new();

    public Task OnBeforeTicketStoreTransactionAsync(
        string userId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task OnTicketStoreUserRowLockedAsync(
        string userId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task OnBeforePasswordChangeUserRowLockAsync(
        string userId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
