using System.Security.Claims;
using System.Security.Cryptography;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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

    private readonly TimeProvider _timeProvider;

    private readonly SessionLifetimeOptions _sessionLifetimeOptions;

    public SqlServerTicketStore(
        IDbContextFactory<SessionTicketDbContext> dbContextFactory,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<SqlServerTicketStore> logger,
        TimeProvider timeProvider,
        IOptions<SessionLifetimeOptions> sessionLifetimeOptions)
    {
        ArgumentNullException.ThrowIfNull(dbContextFactory);
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(sessionLifetimeOptions);

        _dbContextFactory = dbContextFactory;
        _logger = logger;
        _timeProvider = timeProvider;
        _sessionLifetimeOptions = sessionLifetimeOptions.Value;

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

        var now = _timeProvider.GetUtcNow();

        var createdUtc = ticket.Properties.IssuedUtc ?? now;
        var requestedExpiresUtc = GetRequiredExpiration(ticket);

        // The absolute deadline is calculated exactly once.
        var absoluteExpiresUtc = createdUtc.Add(
            _sessionLifetimeOptions.AbsoluteLifetime);

        // Never allow the initial ticket to exceed the absolute deadline.
        var expiresUtc = requestedExpiresUtc < absoluteExpiresUtc
            ? requestedExpiresUtc
            : absoluteExpiresUtc;

        if (absoluteExpiresUtc <= now ||
            expiresUtc <= now ||
            expiresUtc <= createdUtc)
        {
            throw new InvalidOperationException(
                "The authentication ticket must have valid future expiration values.");
        }

        // Keep the protected ticket consistent with the SQL expiration.
        ticket.Properties.ExpiresUtc = expiresUtc;

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
            ExpiresUtc = expiresUtc,
            AbsoluteExpiresUtc = absoluteExpiresUtc
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

        var now = _timeProvider.GetUtcNow();

        var isIdleExpired = entity.ExpiresUtc <= now;
        var isAbsoluteExpired = entity.AbsoluteExpiresUtc <= now;

        if (isIdleExpired || isAbsoluteExpired)
        {
            // Delete only if the exact expiration state still exists.
            // This prevents deleting a session that was concurrently renewed.
            await dbContext.AuthenticationTickets
                .Where(ticket =>
                    ticket.Key == entity.Key &&
                    ticket.ExpiresUtc == entity.ExpiresUtc &&
                    ticket.AbsoluteExpiresUtc == entity.AbsoluteExpiresUtc &&
                    (ticket.ExpiresUtc <= now ||
                     ticket.AbsoluteExpiresUtc <= now))
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

        var now = _timeProvider.GetUtcNow();
        var requestedExpiresUtc = GetRequiredExpiration(ticket);

        if (requestedExpiresUtc <= now)
        {
            return;
        }

        await using var dbContext =
            await _dbContextFactory.CreateDbContextAsync(
                cancellationToken);

        // Read the immutable absolute deadline from the existing session.
        // Renewal is not allowed to create or replace the session.
        DateTimeOffset? absoluteExpiresUtc =
            await dbContext.AuthenticationTickets
                .AsNoTracking()
                .Where(existing =>
                    existing.Key == key &&
                    existing.UserId == metadata.UserId &&
                    existing.AccountKind == metadata.AccountKind &&
                    existing.StoreId == metadata.StoreId &&
                    existing.ExpiresUtc > now &&
                    existing.AbsoluteExpiresUtc > now)
                .Select(existing =>
                    (DateTimeOffset?)existing.AbsoluteExpiresUtc)
                .SingleOrDefaultAsync(cancellationToken);

        if (!absoluteExpiresUtc.HasValue)
        {
            _logger.LogDebug(
                "Session renewal was ignored because the session was missing, " +
                "expired, or identity-mismatched.");
            return;
        }

        // Clamp sliding expiration to the immutable absolute deadline.
        var effectiveExpiresUtc = requestedExpiresUtc < absoluteExpiresUtc.Value
            ? requestedExpiresUtc
            : absoluteExpiresUtc.Value;

        if (effectiveExpiresUtc <= now)
        {
            return;
        }

        // The handler uses this ticket after RenewAsync completes.
        // Updating the property here ensures that the protected ticket,
        // the SQL expiration, and the handler's refreshed ticket agree.
        ticket.Properties.ExpiresUtc = effectiveExpiresUtc;

        var protectedTicket = ProtectTicket(ticket);

        // The update remains conditional:
        //
        // 1. Same session key.
        // 2. Same user/account/store identity.
        // 3. Idle expiration has not passed.
        // 4. Absolute expiration has not passed.
        // 5. Absolute deadline has not changed.
        // 6. Incoming expiration is later than current expiration.
        //
        // There is deliberately no INSERT fallback.
        var affectedRows = await dbContext.AuthenticationTickets
            .Where(existing =>
                existing.Key == key &&
                existing.UserId == metadata.UserId &&
                existing.AccountKind == metadata.AccountKind &&
                existing.StoreId == metadata.StoreId &&
                existing.ExpiresUtc > now &&
                existing.AbsoluteExpiresUtc > now &&
                existing.AbsoluteExpiresUtc == absoluteExpiresUtc.Value &&
                existing.ExpiresUtc < effectiveExpiresUtc)
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
                        effectiveExpiresUtc),
                cancellationToken);

        if (affectedRows == 0)
        {
            _logger.LogDebug(
                "Session renewal was ignored because the session was missing, " +
                "expired, identity-mismatched, or already renewed to an equal " +
                "or later expiration.");
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
