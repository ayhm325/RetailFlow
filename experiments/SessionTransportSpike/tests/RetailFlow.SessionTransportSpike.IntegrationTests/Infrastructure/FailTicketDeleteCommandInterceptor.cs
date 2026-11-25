using System.Data.Common;

using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RetailFlow.SessionTransportSpike.IntegrationTests.Infrastructure;

/// <summary>
/// Intercepts the AuthenticationSessionTickets DELETE command
/// and replaces it with a real SQL Server THROW statement.
///
/// This is test-only fault injection.
/// </summary>
internal sealed class FailTicketDeleteCommandInterceptor : DbCommandInterceptor
{
    private int _triggered;

    public bool WasTriggered => Volatile.Read(ref _triggered) == 1;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var sql = command.CommandText.TrimStart();

        var isTicketDelete =
            sql.StartsWith(
                "DELETE",
                StringComparison.OrdinalIgnoreCase) &&
            sql.Contains(
                "AuthenticationSessionTickets",
                StringComparison.OrdinalIgnoreCase);

        if (isTicketDelete &&
            Interlocked.CompareExchange(ref _triggered, 1, 0) == 0)
        {
            // Remove parameters belonging to the original DELETE.
            command.Parameters.Clear();

            // Ask SQL Server itself to raise an error.
            command.CommandText = """
                THROW 51014, N'ST-14 injected ticket DELETE failure.', 1;
                """;
        }

        return base.NonQueryExecutingAsync(
            command,
            eventData,
            result,
            cancellationToken);
    }
}
