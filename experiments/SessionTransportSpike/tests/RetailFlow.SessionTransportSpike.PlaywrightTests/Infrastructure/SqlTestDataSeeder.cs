using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Domain.Stores;
using RetailFlow.IdentitySpike.Api.Infrastructure.Persistence;
using RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

namespace RetailFlow.SessionTransportSpike.PlaywrightTests.Infrastructure;

public sealed record SeededTenant(
    Guid StoreId,
    string Slug,
    string Email,
    string Password,
    string UserId);

public sealed class SqlTestDataSeeder
{
    private readonly string _connectionString;
    private readonly PasswordHasher<ApplicationUser> _passwordHasher = new();

    public SqlTestDataSeeder(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<SeededTenant> SeedStoreCustomerAsync(
        string slugPrefix,
        string? email = null,
        string password = "Playwright!Pass#2026_LongEnough")
    {
        var storeId = Guid.NewGuid();
        var uniqueSuffix = Guid.NewGuid().ToString("N");
        var slug = $"{slugPrefix}-{uniqueSuffix}".ToLowerInvariant();
        var resolvedEmail = email ?? $"customer-{uniqueSuffix}@retailflow.test";
        var normalizedEmail = resolvedEmail.ToUpperInvariant();
        var userId = Guid.NewGuid().ToString("D");
        var userName = $"cust-{uniqueSuffix}";
        var normalizedUserName = userName.ToUpperInvariant();

        await using var identityDb = CreateIdentityDbContext();

        var store = new SpikeStore
        {
            Id = storeId,
            Name = $"Store {slugPrefix} {uniqueSuffix[..8]}",
            Slug = slug,
            IsActive = true
        };

        var user = new ApplicationUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = normalizedUserName,
            Email = resolvedEmail,
            NormalizedEmail = normalizedEmail,
            EmailConfirmed = true,
            AccountKind = AccountKind.StoreCustomer,
            StoreId = storeId,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            LockoutEnabled = true,
            AccessFailedCount = 0
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        identityDb.Stores.Add(store);
        identityDb.Users.Add(user);

        await identityDb.SaveChangesAsync();

        return new SeededTenant(
            storeId,
            slug,
            resolvedEmail,
            password,
            userId);
    }

    public async Task CleanupAsync(IEnumerable<SeededTenant> tenants)
    {
        var list = tenants.ToList();

        if (list.Count == 0)
        {
            return;
        }

        var userIds = list.Select(t => t.UserId).ToArray();
        var storeIds = list.Select(t => t.StoreId).ToArray();

        // 1. Delete AuthenticationSessionTickets first.
        //    Never delete shared DataProtectionKeys during test cleanup.
        await using (var ticketDb = CreateTicketDbContext())
        {
            await ticketDb.AuthenticationTickets
                .Where(ticket =>
                    userIds.Contains(ticket.UserId) ||
                    storeIds.Contains(ticket.StoreId))
                .ExecuteDeleteAsync();
        }

        // 2. Delete Identity users next.
        // 3. Delete Stores last.
        await using (var identityDb = CreateIdentityDbContext())
        {
            await identityDb.Users
                .Where(user => userIds.Contains(user.Id))
                .ExecuteDeleteAsync();

            await identityDb.Stores
                .Where(store => storeIds.Contains(store.Id))
                .ExecuteDeleteAsync();
        }
    }

    private IdentitySpikeDbContext CreateIdentityDbContext()
    {
        var options = new DbContextOptionsBuilder<IdentitySpikeDbContext>()
            .UseSqlServer(_connectionString)
            .Options;

        return new IdentitySpikeDbContext(options);
    }

    private SessionTicketDbContext CreateTicketDbContext()
    {
        var options = new DbContextOptionsBuilder<SessionTicketDbContext>()
            .UseSqlServer(
                _connectionString,
                sqlServer => sqlServer.MigrationsHistoryTable(
                    SessionTicketDbContextFactory.MigrationsHistoryTableName))
            .Options;

        return new SessionTicketDbContext(options);
    }
}
