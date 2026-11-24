using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

using RetailFlow.IdentitySpike.Api.Domain.Identity;
using RetailFlow.IdentitySpike.Api.Infrastructure.Identity;
using RetailFlow.SessionTransportSpike.Api.Authentication;

namespace RetailFlow.SessionTransportSpike.Api.Infrastructure.Persistence;

/// <summary>
/// Stores complete authentication tickets in SQL Server.
///
/// The browser receives only an opaque session key.
/// The complete ticket is protected with ASP.NET Core Data Protection.
/// </summary>
public sealed class SqlServerTicketStore : ITicketStore
{
    private const int SessionKeyBytes = 32;
    private const int SessionKeyLength = 43;

    private readonly IDbContextFactory<SessionTicketDbContext>
        _dbContextFactory;

    private readonly IDataProtector _ticketProtector;

    private readonly ILogger<SqlServerTicketStore> _logger;

    public SqlServerTicketStore(
        IDbContextFactory<SessionTicketDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SqlServerTicketStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _dbContextFactory = dbContextFactory;
        _logger = logger;

        _ticketProtector = dataProtectionProvider.CreateProtector(
            "RetailFlow",
            "SessionTransport",
            "SqlServerTicketStore",
            "AuthenticationTicket",
            "v1");
    }

    // -------------------------------------------------------------------------
    // StoreAsync
    // -------------------------------------------------------------------------

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        return StoreCoreAsync(ticket, CancellationToken.None);
    }

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        return StoreCoreAsync(ticket, cancellationToken);
    }

    public Task<string> StoreAsync(
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return StoreCoreAsync(ticket, cancellationToken);
    }

    private async Task<string> StoreCoreAsync(
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        var metadata = ExtractRequiredMetadata(ticket);

        var now = DateTimeOffset.UtcNow;

        var createdUtc = ticket.Properties.IssuedUtc ?? now;
        var expiresUtc = GetRequiredExpiration(ticket);

        if (expiresUtc <= now || expiresUtc <= createdUtc)
        {
            throw new InvalidOperationException(
                "The authentication ticket must have a valid future expiration.");
        }

        var sessionKey = GenerateSessionKey();

        var protectedTicket = ProtectTicket(ticket);

        var entity = new PersistedAuthenticationTicket
        {
            Key = sessionKey,
            UserId = metadata.UserId,
            AccountKind = metadata.AccountKind,
            StoreId = metadata.StoreId,
            ProtectedTicket = protectedTicket,
            CreatedUtc = createdUtc,
            LastRenewedUtc = now,
            ExpiresUtc = expiresUtc
        };

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        dbContext.AuthenticationTickets.Add(entity);

        await dbContext.SaveChangesAsync(cancellationToken);

        return sessionKey;
    }

    // -------------------------------------------------------------------------
    // RetrieveAsync
    // -------------------------------------------------------------------------

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        return RetrieveCoreAsync(key, CancellationToken.None);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        return RetrieveCoreAsync(key, cancellationToken);
    }

    public Task<AuthenticationTicket?> RetrieveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return RetrieveCoreAsync(key, cancellationToken);
    }

    private async Task<AuthenticationTicket?> RetrieveCoreAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (!IsValidSessionKey(key))
        {
            return null;
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        var entity = await dbContext.AuthenticationTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(
                ticket => ticket.Key == key,
                cancellationToken);

        if (entity is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;

        if (entity.ExpiresUtc <= now)
        {
            // Delete only if this exact expiration state still exists.
            // A concurrent renewal may already have extended the session.
            await dbContext.AuthenticationTickets
                .Where(ticket =>
                    ticket.Key == entity.Key &&
                    ticket.ExpiresUtc == entity.ExpiresUtc &&
                    ticket.ExpiresUtc <= now)
                .ExecuteDeleteAsync(cancellationToken);

            return null;
        }

        AuthenticationTicket? ticket;

        try
        {
            var serializedTicket =
                _ticketProtector.Unprotect(entity.ProtectedTicket);

            ticket = TicketSerializer.Default.Deserialize(
                serializedTicket);
        }
        catch (CryptographicException exception)
        {
            _logger.LogWarning(
                exception,
                "The stored authentication ticket failed Data Protection validation.");

            return null;
        }
        catch (IOException exception)
        {
            _logger.LogWarning(
                exception,
                "The stored authentication ticket could not be deserialized.");

            return null;
        }
        catch (FormatException exception)
        {
            _logger.LogWarning(
                exception,
                "The stored authentication ticket has an invalid format.");

            return null;
        }

        if (ticket is null)
        {
            return null;
        }

        if (!TryExtractMetadata(ticket, out var metadata))
        {
            _logger.LogWarning(
                "A stored authentication ticket did not contain a valid StoreCustomer identity.");

            return null;
        }

        var ticketExpiresUtc = ticket.Properties.ExpiresUtc;

        var metadataMatches =
            string.Equals(
                metadata.UserId,
                entity.UserId,
                StringComparison.Ordinal) &&
            metadata.AccountKind == entity.AccountKind &&
            metadata.StoreId == entity.StoreId;

        var expirationMatches =
            ticketExpiresUtc.HasValue &&
            ticketExpiresUtc.Value == entity.ExpiresUtc &&
            ticketExpiresUtc.Value > now;

        if (!metadataMatches || !expirationMatches)
        {
            _logger.LogWarning(
                "The stored authentication ticket metadata or expiration did not match its SQL record.");

            return null;
        }

        return ticket;
    }

    // -------------------------------------------------------------------------
    // RenewAsync
    // -------------------------------------------------------------------------

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket)
    {
        return RenewCoreAsync(
            key,
            ticket,
            CancellationToken.None);
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        return RenewCoreAsync(key, ticket, cancellationToken);
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return RenewCoreAsync(key, ticket, cancellationToken);
    }

    private async Task RenewCoreAsync(
        string key,
        AuthenticationTicket ticket,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (!IsValidSessionKey(key))
        {
            return;
        }

        var metadata = ExtractRequiredMetadata(ticket);

        var now = DateTimeOffset.UtcNow;
        var expiresUtc = GetRequiredExpiration(ticket);

        if (expiresUtc <= now)
        {
            return;
        }

        var protectedTicket = ProtectTicket(ticket);

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        // The session identity is immutable.
        //
        // Update only when:
        // - The key exists.
        // - UserId still matches.
        // - AccountKind still matches.
        // - StoreId still matches.
        // - The previous session has not expired.
        //
        // If Logout already deleted the row, this update affects zero rows.
        // It must never insert a replacement session.
        var affectedRows = await dbContext.AuthenticationTickets
            .Where(existing =>
                existing.Key == key &&
                existing.UserId == metadata.UserId &&
                existing.AccountKind == metadata.AccountKind &&
                existing.StoreId == metadata.StoreId &&
                existing.ExpiresUtc > now)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        existing => existing.ProtectedTicket,
                        protectedTicket)
                    .SetProperty(
                        existing => existing.LastRenewedUtc,
                        now)
                    .SetProperty(
                        existing => existing.ExpiresUtc,
                        expiresUtc),
                cancellationToken);

        if (affectedRows == 0)
        {
            _logger.LogDebug(
                "Session renewal was ignored because the session was missing, expired, or its identity did not match.");
        }
    }

    // -------------------------------------------------------------------------
    // RemoveAsync
    // -------------------------------------------------------------------------

    public Task RemoveAsync(string key)
    {
        return RemoveCoreAsync(key, CancellationToken.None);
    }

    public Task RemoveAsync(
        string key,
        CancellationToken cancellationToken)
    {
        return RemoveCoreAsync(key, cancellationToken);
    }

    public Task RemoveAsync(
        string key,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return RemoveCoreAsync(key, cancellationToken);
    }

    private async Task RemoveCoreAsync(
        string key,
        CancellationToken cancellationToken)
    {
        if (!IsValidSessionKey(key))
        {
            return;
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        await dbContext.AuthenticationTickets
            .Where(ticket => ticket.Key == key)
            .ExecuteDeleteAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Ticket protection
    // -------------------------------------------------------------------------

    private byte[] ProtectTicket(AuthenticationTicket ticket)
    {
        var serializedTicket =
            TicketSerializer.Default.Serialize(ticket);

        return _ticketProtector.Protect(serializedTicket);
    }

    private static DateTimeOffset GetRequiredExpiration(
        AuthenticationTicket ticket)
    {
        if (!ticket.Properties.ExpiresUtc.HasValue)
        {
            throw new InvalidOperationException(
                "The authentication ticket must have ExpiresUtc.");
        }

        return ticket.Properties.ExpiresUtc.Value;
    }

    // -------------------------------------------------------------------------
    // StoreCustomer metadata validation
    // -------------------------------------------------------------------------

    private static TicketMetadata ExtractRequiredMetadata(
        AuthenticationTicket ticket)
    {
        if (!TryExtractMetadata(ticket, out var metadata))
        {
            throw new InvalidOperationException(
                "The ticket must contain one valid StoreCustomer identity.");
        }

        return metadata;
    }

    private static bool TryExtractMetadata(
        AuthenticationTicket ticket,
        out TicketMetadata metadata)
    {
        metadata = default!;

        if (!string.Equals(
                ticket.AuthenticationScheme,
                IdentityConstants.ApplicationScheme,
                StringComparison.Ordinal))
        {
            return false;
        }

        var principal = ticket.Principal;

        var userIdClaims = principal
            .FindAll(ClaimTypes.NameIdentifier)
            .Select(claim => claim.Value)
            .ToArray();

        var accountKindClaims = principal
            .FindAll(RetailFlowClaimTypes.AccountKind)
            .Select(claim => claim.Value)
            .ToArray();

        var storeIdClaims = principal
            .FindAll(RetailFlowClaimTypes.StoreId)
            .Select(claim => claim.Value)
            .ToArray();

        if (userIdClaims.Length != 1 ||
            string.IsNullOrWhiteSpace(userIdClaims[0]))
        {
            return false;
        }

        if (accountKindClaims.Length != 1 ||
            !string.Equals(
                accountKindClaims[0],
                AccountKind.StoreCustomer.ToString(),
                StringComparison.Ordinal))
        {
            return false;
        }

        if (storeIdClaims.Length != 1 ||
            !Guid.TryParseExact(
                storeIdClaims[0],
                "D",
                out var storeId) ||
            storeId == Guid.Empty)
        {
            return false;
        }

        metadata = new TicketMetadata(
            userIdClaims[0],
            AccountKind.StoreCustomer,
            storeId);

        return true;
    }

    // -------------------------------------------------------------------------
    // Opaque session key
    // -------------------------------------------------------------------------

    private static string GenerateSessionKey()
    {
        var randomBytes =
            RandomNumberGenerator.GetBytes(SessionKeyBytes);

        return WebEncoders.Base64UrlEncode(randomBytes);
    }

    private static bool IsValidSessionKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key) ||
            key.Length != SessionKeyLength)
        {
            return false;
        }

        return key.All(character =>
            char.IsAsciiLetterOrDigit(character) ||
            character is '-' or '_');
    }

    private sealed record TicketMetadata(
        string UserId,
        AccountKind AccountKind,
        Guid StoreId);
}
