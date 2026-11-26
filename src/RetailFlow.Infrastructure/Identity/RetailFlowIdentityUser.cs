using Microsoft.AspNetCore.Identity;

namespace RetailFlow.Infrastructure.Identity;

public enum MfaEnrollmentStatus
{
    NotEnrolled = 0,
    EnrollmentPending = 1,
    Enabled = 2,
    ReplacementPending = 3
}

/// <summary>
/// ASP.NET Core Identity user entity for RetailFlow with store-scoped tenancy and MFA security state.
/// Uses Guid primary keys to preserve existing Orders, CheckoutRequests, and SessionTickets references.
/// </summary>
public sealed class RetailFlowIdentityUser : IdentityUser<Guid>
{
    public Guid? StoreId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }

    public MfaEnrollmentStatus MfaEnrollmentStatus { get; set; } = MfaEnrollmentStatus.NotEnrolled;

    public DateTimeOffset? MfaPendingExpiresAtUtc { get; set; }

    public string? ProtectedPendingAuthenticatorKey { get; set; }

    public int MfaFailedAttempts { get; set; }

    public DateTimeOffset? MfaLockoutEndUtc { get; set; }

    public long? LastUsedTotpTimeStep { get; set; }
}
