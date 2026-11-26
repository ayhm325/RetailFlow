using System.Globalization;
using System.Security.Claims;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

using RetailFlow.Domain.Identity;
using RetailFlow.Infrastructure.Persistence;

namespace RetailFlow.Api.Auth;

/// <summary>
/// SQL Server-backed ITicketStore per AUTH-ADR-01 (Accepted 2026-11-26),
/// enforcing immutable per-role AbsoluteExpiresAtUtc alongside clamped sliding idle ExpiresAtUtc.
/// </summary>
public sealed class SqlSessionTicketStore : ITicketStore
{
    public const string AbsoluteExpirationPropertyKey = "rf_abs_exp";

    private static readonly TimeSpan PlatformAdminIdleLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan PlatformAdminAbsoluteLifetime = TimeSpan.FromHours(8);

    private static readonly TimeSpan MerchantIdleLifetime = TimeSpan.FromHours(4);
    private static readonly TimeSpan MerchantAbsoluteLifetime = TimeSpan.FromHours(24);

    private static readonly TimeSpan CustomerIdleLifetime = TimeSpan.FromHours(12);
    private static readonly TimeSpan CustomerAbsoluteLifetime = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SqlSessionTicketStore> _logger;

    public SqlSessionTicketStore(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<SqlSessionTicketStore> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();

        var (idleLifetime, absoluteLifetime) = GetLifetimes(ticket.Principal);
        var absoluteExpiresAt = now.Add(absoluteLifetime);
        var idleExpiresAt = now.Add(idleLifetime);
        var effectiveExpiresAt = idleExpiresAt < absoluteExpiresAt ? idleExpiresAt : absoluteExpiresAt;

        ticket.Properties.IssuedUtc ??= now;
        ticket.Properties.ExpiresUtc = effectiveExpiresAt;
        ticket.Properties.Items[AbsoluteExpirationPropertyKey] =
            absoluteExpiresAt.ToString("O", CultureInfo.InvariantCulture);

        var userIdClaim = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var userId = Guid.TryParse(userIdClaim, out var parsedUserId) ? parsedUserId : Guid.Empty;
        var storeIdClaim = ticket.Principal.FindFirstValue("store_id");
        Guid? storeId = Guid.TryParse(storeIdClaim, out var parsedStoreId) ? parsedStoreId : null;

        var serialized = TicketSerializer.Default.Serialize(ticket);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        db.SessionTickets.Add(SessionTicket.Create(
            key,
            userId,
            storeId,
            serialized,
            effectiveExpiresAt,
            now,
            createdAtUtc: now,
            absoluteExpiresAtUtc: absoluteExpiresAt));
        await db.SaveChangesAsync();

        _logger.LogInformation(
            "Audit: SessionCreated UserId={UserId} AbsoluteExpiresAtUtc={AbsoluteExpiresAtUtc}",
            userId,
            absoluteExpiresAt);

        return key;
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var now = _timeProvider.GetUtcNow();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        var existing = await db.SessionTickets.SingleOrDefaultAsync(t => t.Key == key);
        if (existing is null)
        {
            return;
        }

        if (existing.AbsoluteExpiresAtUtc <= now)
        {
            db.SessionTickets.Remove(existing);
            await db.SaveChangesAsync();
            _logger.LogInformation("Audit: SessionAbsoluteTimeoutReached UserId={UserId}", existing.UserId);
            return;
        }

        var (idleLifetime, _) = GetLifetimes(ticket.Principal);
        var idleExpiresAt = now.Add(idleLifetime);
        var clampedExpiresAt = idleExpiresAt < existing.AbsoluteExpiresAtUtc
            ? idleExpiresAt
            : existing.AbsoluteExpiresAtUtc;

        ticket.Properties.ExpiresUtc = clampedExpiresAt;
        ticket.Properties.Items[AbsoluteExpirationPropertyKey] =
            existing.AbsoluteExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture);

        var serialized = TicketSerializer.Default.Serialize(ticket);
        existing.Renew(serialized, clampedExpiresAt, now);
        await db.SaveChangesAsync();
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var now = _timeProvider.GetUtcNow();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        var entity = await db.SessionTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Key == key);

        if (entity is null || entity.ExpiresAtUtc <= now || entity.AbsoluteExpiresAtUtc <= now)
        {
            return null;
        }

        var ticket = TicketSerializer.Default.Deserialize(entity.Value);
        if (ticket is null)
        {
            return null;
        }

        if (!ticket.Properties.ExpiresUtc.HasValue || ticket.Properties.ExpiresUtc.Value > entity.AbsoluteExpiresAtUtc)
        {
            ticket.Properties.ExpiresUtc = entity.AbsoluteExpiresAtUtc;
        }

        ticket.Properties.Items[AbsoluteExpirationPropertyKey] =
            entity.AbsoluteExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture);

        return ticket;
    }

    public async Task RemoveAsync(string key)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<RetailFlowDbContext>();

        await db.SessionTickets
            .Where(t => t.Key == key)
            .ExecuteDeleteAsync();
    }

    internal static (TimeSpan IdleLifetime, TimeSpan AbsoluteLifetime) GetLifetimes(ClaimsPrincipal principal)
    {
        if (principal.IsInRole(nameof(AppRole.PlatformAdmin)))
        {
            return (PlatformAdminIdleLifetime, PlatformAdminAbsoluteLifetime);
        }

        if (principal.IsInRole(nameof(AppRole.MerchantOwner)))
        {
            return (MerchantIdleLifetime, MerchantAbsoluteLifetime);
        }

        return (CustomerIdleLifetime, CustomerAbsoluteLifetime);
    }
}
