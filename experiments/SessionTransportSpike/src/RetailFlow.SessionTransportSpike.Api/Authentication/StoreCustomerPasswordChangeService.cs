using System.Data;

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

    private readonly IStoreCustomerSessionOperationHook _operationHook;

    public StoreCustomerPasswordChangeService(
        IdentitySpikeDbContext identityDbContext,
        UserManager<ApplicationUser> userManager,
        IIdentityLookupScopeAccessor scopeAccessor,
        IStoreCustomerSessionOperationHook? operationHook = null)
    {
        ArgumentNullException.ThrowIfNull(identityDbContext);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(scopeAccessor);

        _identityDbContext = identityDbContext;
        _userManager = userManager;
        _scopeAccessor = scopeAccessor;
        _operationHook = operationHook
            ?? NoOpStoreCustomerSessionOperationHook.Instance;
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
        // 3. Lock the user row first (same lock order as SqlServerTicketStore)
        //    and create SessionTicketDbContext using the SAME DbConnection.
        // -------------------------------------------------------------

        var sharedConnection =
            _identityDbContext.Database.GetDbConnection();

        await AcquireUserRowUpdateLockAsync(
            sharedConnection,
            transaction.GetDbTransaction(),
            user.Id,
            AccountKind.StoreCustomer,
            storeId,
            cancellationToken);

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

        await _operationHook.OnBeforePasswordChangeTicketDeleteAsync(
            ticketDbContext,
            user.Id,
            storeId,
            cancellationToken);

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

    private static async Task AcquireUserRowUpdateLockAsync(
        System.Data.Common.DbConnection connection,
        System.Data.Common.DbTransaction transaction,
        string userId,
        AccountKind accountKind,
        Guid storeId,
        CancellationToken cancellationToken)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT [Id]
            FROM [dbo].[AspNetUsers] WITH (UPDLOCK, ROWLOCK)
            WHERE [Id] = @userId
              AND [AccountKind] = @accountKind
              AND [StoreId] = @storeId;
            """;

        var userIdParam = command.CreateParameter();
        userIdParam.ParameterName = "@userId";
        userIdParam.DbType = DbType.String;
        userIdParam.Value = userId;
        command.Parameters.Add(userIdParam);

        var accountKindParam = command.CreateParameter();
        accountKindParam.ParameterName = "@accountKind";
        accountKindParam.DbType = DbType.Int32;
        accountKindParam.Value = (int)accountKind;
        command.Parameters.Add(accountKindParam);

        var storeIdParam = command.CreateParameter();
        storeIdParam.ParameterName = "@storeId";
        storeIdParam.DbType = DbType.Guid;
        storeIdParam.Value = storeId;
        command.Parameters.Add(storeIdParam);

        _ = await command.ExecuteScalarAsync(cancellationToken);
    }
}
