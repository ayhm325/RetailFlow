using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Auth;

/// <summary>
/// SQL Server-backed ITicketStore per AUTH-ADR-01 (Accepted 2026-11-26).
/// </summary>
public sealed class SqlSessionTicketStore : ITicketStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public SqlSessionTicketStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var expiresAt = ticket.Properties.ExpiresUtc ?? now.AddHours(12);

        var userIdClaim = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : Guid.Empty;
        var storeIdClaim = ticket.Principal.FindFirstValue("store_id");
        Guid? storeId = Guid.TryParse(storeIdClaim, out var parsedStoreId) ? parsedStoreId : null;

        var serialized = TicketSerializer.Default.Serialize(ticket);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        db.SessionTickets.Add(SessionTicket.Create(key, userId, storeId, serialized, expiresAt, now));
        await db.SaveChangesAsync();

        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = ticket.Properties.ExpiresUtc ?? now.AddHours(12);
        var serialized = TicketSerializer.Default.Serialize(ticket);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        var existing = await db.SessionTickets.SingleOrDefaultAsync(t => t.Key == key);
        if (existing is not null)
        {
            existing.Renew(serialized, expiresAt, now);
            await db.SaveChangesAsync();
        }
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        var entity = await db.SessionTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Key == key);

        if (entity is null || entity.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        return TicketSerializer.Default.Deserialize(entity.Value);
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        await db.SessionTickets
            .Where(t => t.Key == key)
            .ExecuteDeleteAsync();
    }
}
