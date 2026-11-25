using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;

using RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

namespace RetailFlow.SessionTransportSpike.Api.Authentication;

public sealed class StoreCustomerPasswordChangeService
    : IStoreCustomerPasswordChangeService
{
    private readonly IdentitySpikeDbContext _identityDbContext;

    private readonly UserManager<ApplicationUser> _userManager;

    private readonly IIdentityLookupScopeAccessor _scopeAccessor;

    public StoreCustomerPasswordChangeService(
        IdentitySpikeDbContext identityDbContext,
        UserManager<ApplicationUser> userManager,
        IIdentityLookupScopeAccessor scopeAccessor)
    {
        ArgumentNullException.ThrowIfNull(identityDbContext);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(scopeAccessor);

        _identityDbContext = identityDbContext;
        _userManager = userManager;
        _scopeAccessor = scopeAccessor;
    }

    public async Task<IdentityResult> ChangePasswordAndRevokeSessionsAsync(
        ApplicationUser user,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrEmpty(currentPassword);
        ArgumentException.ThrowIfNullOrEmpty(newPassword);

        // -------------------------------------------------------------
        // 1. Enforce StoreCustomer scope.
        // -------------------------------------------------------------

        var scope = _scopeAccessor.Current
            ?? throw new InvalidOperationException(
                "Identity lookup scope has not been established.");

        if (user.AccountKind != AccountKind.StoreCustomer ||
            user.StoreId is not Guid storeId ||
            storeId == Guid.Empty ||
            scope.AccountKind != AccountKind.StoreCustomer ||
            scope.StoreId != storeId)
        {
            throw new InvalidOperationException(
                "Password changes are restricted to the current StoreCustomer scope.");
        }

        // -------------------------------------------------------------
        // 2. Begin one SQL Server transaction through IdentityDbContext.
        // -------------------------------------------------------------

        await using var transaction =
            await _identityDbContext.Database.BeginTransactionAsync(
                cancellationToken);

        // -------------------------------------------------------------
        // 3. Create SessionTicketDbContext using the SAME DbConnection.
        // -------------------------------------------------------------

        var sharedConnection =
            _identityDbContext.Database.GetDbConnection();

        var ticketOptions =
            new DbContextOptionsBuilder<SessionTicketDbContext>()
                .UseSqlServer(
                    sharedConnection,
                    sqlServer =>
                    {
                        sqlServer.MigrationsHistoryTable(
                            SessionTicketDbContextFactory.MigrationsHistoryTableName);
                    })
                .Options;

        await using var ticketDbContext =
            new SessionTicketDbContext(ticketOptions);

        // Enlist the second DbContext in Identity's transaction.
        await ticketDbContext.Database.UseTransactionAsync(
            transaction.GetDbTransaction(),
            cancellationToken);

        // -------------------------------------------------------------
        // 4. Change password through ASP.NET Core Identity.
        // -------------------------------------------------------------

        var result = await _userManager.ChangePasswordAsync(
            user,
            currentPassword,
            newPassword);

        if (!result.Succeeded)
        {
            // No ticket deletion is allowed when Identity validation fails.
            await transaction.RollbackAsync(
                CancellationToken.None);

            return result;
        }

        // -------------------------------------------------------------
        // 5. Revoke every session belonging to this exact user/scope.
        // -------------------------------------------------------------

        await ticketDbContext.AuthenticationTickets
            .Where(ticket =>
                ticket.UserId == user.Id &&
                ticket.AccountKind == AccountKind.StoreCustomer &&
                ticket.StoreId == storeId)
            .ExecuteDeleteAsync(cancellationToken);

        // -------------------------------------------------------------
        // 6. Commit both operations atomically.
        // -------------------------------------------------------------

        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}
