namespace RetailFlow.SessionTransportSpike.Api.Authentication;

/// <summary>
/// Thrown when a persisted session ticket cannot be issued because the user's
/// SecurityStamp changed before the ticket was stored.
/// </summary>
public sealed class StaleSecurityStampException : InvalidOperationException
{
    public StaleSecurityStampException(string message)
        : base(message)
    {
    }
}
