using RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

/// <summary>
/// Optional coordination hook used by integration tests to deterministically
/// exercise concurrency races and transaction rollback behavior.
/// </summary>
public interface IStoreCustomerSessionOperationHook
{
    Task OnBeforeTicketStoreTransactionAsync(
        string userId,
        CancellationToken cancellationToken);

    Task OnTicketStoreUserRowLockedAsync(
        string userId,
        CancellationToken cancellationToken);

    Task OnBeforePasswordChangeTicketDeleteAsync(
        SessionTicketDbContext ticketDbContext,
        string userId,
        Guid storeId,
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

    public Task OnBeforePasswordChangeTicketDeleteAsync(
        SessionTicketDbContext ticketDbContext,
        string userId,
        Guid storeId,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
